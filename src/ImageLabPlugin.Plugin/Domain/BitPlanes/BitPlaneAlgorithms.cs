using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Domain.BitPlanes;

/// <summary>从未预乘 RGBA8888 图片抽取一个离散 8 位通道。</summary>
/// <remarks>Y 先按 BT.601 全范围公式计算，再以中点取偶量化为 byte；不能拆解 double 的存储位。</remarks>
internal sealed class BitPlaneChannelExtractor
{
    public BytePlane Extract(PixelImage source, BitPlaneChannel channel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var values = new byte[checked((int)source.Size.PixelCount)];
        for (var y = 0; y < source.Size.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < source.Size.Width; x++)
            {
                var (red, green, blue, alpha) = source.GetPixel(x, y);
                values[(y * source.Size.Width) + x] = channel switch
                {
                    BitPlaneChannel.Red => red,
                    BitPlaneChannel.Green => green,
                    BitPlaneChannel.Blue => blue,
                    BitPlaneChannel.Alpha => alpha,
                    BitPlaneChannel.Luma => YCbCrColorSpace.QuantizeLuma(red, green, blue),
                    _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "未知位平面通道。")
                };
            }
        }

        return new BytePlane(source.Size, channel, values);
    }
}

/// <summary>一次扫描同时计算 bit 0–7 的计数、比例和二元熵。</summary>
internal sealed class BitPlaneStatisticsAnalyzer
{
    public IReadOnlyList<BitPlaneStatistics> Analyze(BytePlane plane, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plane);
        var ones = new long[8];
        var values = plane.Values.Span;
        for (var i = 0; i < values.Length; i++)
        {
            if ((i & 0xFFFF) == 0) cancellationToken.ThrowIfCancellationRequested();
            var value = values[i];
            for (var bit = 0; bit < 8; bit++) ones[bit] += (value >> bit) & 1;
        }

        var result = new BitPlaneStatistics[8];
        for (var bit = 0; bit < 8; bit++)
        {
            var ratio = ones[bit] / (double)values.Length;
            var entropy = ratio is 0d or 1d ? 0d :
                (-ratio * Math.Log2(ratio)) - ((1d - ratio) * Math.Log2(1d - ratio));
            result[bit] = new BitPlaneStatistics(bit, 1 << bit, values.Length - ones[bit], ones[bit], ratio, entropy);
        }
        return result;
    }
}

/// <summary>只替换所选通道，生成完整尺寸或单个预览像素的确定性重建。</summary>
internal sealed class BitPlaneReconstructor
{
    public BitPlaneReconstructionResult Reconstruct(
        PixelImage source,
        BytePlane plane,
        BitMask8 mask,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(plane);
        if (source.Size != plane.Size) throw new ArgumentException("通道尺寸与源图片不一致。", nameof(plane));
        var result = source.Clone();
        var output = result.WritableRgba;
        var clipped = 0;
        for (var y = 0; y < source.Size.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < source.Size.Width; x++)
            {
                var index = (y * source.Size.Width) + x;
                var offset = index * 4;
                clipped += ApplyPixel(source.Rgba.Span.Slice(offset, 4), output.Slice(offset, 4), plane.Values.Span[index], plane.Channel, mask) ? 1 : 0;
            }
        }
        return new BitPlaneReconstructionResult(result, clipped);
    }

    internal static bool ApplyPixel(ReadOnlySpan<byte> sourcePixel, Span<byte> outputPixel, byte channelValue, BitPlaneChannel channel, BitMask8 mask)
    {
        var kept = mask.Apply(channelValue);
        if (channel is BitPlaneChannel.Red or BitPlaneChannel.Green or BitPlaneChannel.Blue or BitPlaneChannel.Alpha)
        {
            var component = channel switch { BitPlaneChannel.Red => 0, BitPlaneChannel.Green => 1, BitPlaneChannel.Blue => 2, _ => 3 };
            outputPixel[component] = kept;
            return false;
        }

        if (mask.Value == byte.MaxValue)
        {
            // Y 的 byte 量化会丢失小数；全掩码表示“不删除任何贡献”，因此用逐字节恒等捷径避免无意义往返误差。
            sourcePixel.CopyTo(outputPixel);
            return false;
        }

        var color = YCbCrColorSpace.FromRgb(sourcePixel[0], sourcePixel[1], sourcePixel[2]);
        var restored = YCbCrColorSpace.ToRgb(kept, color.ChromaBlue, color.ChromaRed);
        outputPixel[0] = YCbCrColorSpace.ClampToByte(restored.Red, out var redClipped);
        outputPixel[1] = YCbCrColorSpace.ClampToByte(restored.Green, out var greenClipped);
        outputPixel[2] = YCbCrColorSpace.ClampToByte(restored.Blue, out var blueClipped);
        return redClipped || greenClipped || blueClipped;
    }
}

/// <summary>从原始样本直接取样，生成四张共享坐标的有界预览。</summary>
/// <remarks>
/// 先在原图字节上抽位，再按坐标取样；禁止先缩放像素再拆位。单位平面的 Alpha 固定 255，
/// 否则观察 Alpha=0 时显示图本身会透明，用户看到的就不是位事实。
/// </remarks>
internal sealed class BitPlaneProjector
{
    public BitPlaneProjection Project(
        PixelImage source,
        BytePlane plane,
        BitMask8 mask,
        int focusedBit,
        int maximumEdge = 1024,
        CancellationToken cancellationToken = default)
    {
        _ = BitMask8.Single(focusedBit);
        if (source.Size != plane.Size) throw new ArgumentException("通道尺寸与源图片不一致。", nameof(plane));
        var map = BitPlanePreviewMap.Create(source.Size, maximumEdge);
        var length = checked((int)(map.PreviewSize.PixelCount * 4));
        var sourceRgba = new byte[length];
        var focusedRgba = new byte[length];
        var combinedRgba = new byte[length];
        var reconstructedRgba = new byte[length];
        var clipped = 0;

        for (var y = 0; y < map.PreviewSize.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < map.PreviewSize.Width; x++)
            {
                var (sourceX, sourceY) = map.GetSourcePoint(x, y);
                var sourceIndex = (sourceY * source.Size.Width) + sourceX;
                var targetOffset = ((y * map.PreviewSize.Width) + x) * 4;
                source.Rgba.Span.Slice(sourceIndex * 4, 4).CopyTo(sourceRgba.AsSpan(targetOffset, 4));
                var value = plane.Values.Span[sourceIndex];
                var bitColor = ((value >> focusedBit) & 1) == 1 ? byte.MaxValue : (byte)0;
                WriteGray(focusedRgba, targetOffset, bitColor);
                WriteGray(combinedRgba, targetOffset, mask.Apply(value));

                source.Rgba.Span.Slice(sourceIndex * 4, 4).CopyTo(reconstructedRgba.AsSpan(targetOffset, 4));
                clipped += BitPlaneReconstructor.ApplyPixel(
                    source.Rgba.Span.Slice(sourceIndex * 4, 4),
                    reconstructedRgba.AsSpan(targetOffset, 4), value, plane.Channel, mask) ? 1 : 0;
            }
        }

        return new BitPlaneProjection(
            new PixelImage(map.PreviewSize, sourceRgba),
            new PixelImage(map.PreviewSize, focusedRgba),
            new PixelImage(map.PreviewSize, combinedRgba),
            new PixelImage(map.PreviewSize, reconstructedRgba),
            map,
            clipped);
    }

    private static void WriteGray(byte[] rgba, int offset, byte value)
    {
        rgba[offset] = rgba[offset + 1] = rgba[offset + 2] = value;
        rgba[offset + 3] = byte.MaxValue;
    }
}

/// <summary>按一个源坐标读取像素事实，不触发重新扫描。</summary>
internal sealed class BitPlanePixelInspector
{
    public BitPlanePixelReport Inspect(PixelImage source, BytePlane plane, BitMask8 mask, int x, int y)
    {
        if (source.Size != plane.Size) throw new ArgumentException("通道尺寸与源图片不一致。", nameof(plane));
        var (red, green, blue, alpha) = source.GetPixel(x, y);
        var value = plane[x, y];
        return new BitPlanePixelReport(x, y, red, green, blue, alpha, value,
            $"0b{Convert.ToString(value, 2).PadLeft(8, '0')}", mask.Value, mask.Apply(value));
    }
}
