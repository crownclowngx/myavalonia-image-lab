using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Domain.BitPlanes;

/// <summary>位平面观察器支持的五个 8 位通道。</summary>
internal enum BitPlaneChannel
{
    Red,
    Green,
    Blue,
    Alpha,
    Luma
}

/// <summary>经过验证的 8 位保留掩码。</summary>
/// <remarks>
/// 掩码自身用 byte 保存，所以 0x00（清空）与 0xFF（全部）同样是合法值。所有位序入口集中在这里：
/// bit 7 的权重为 128，bit 0 的权重为 1，UI 和 Document 不再各自手写位移公式。
/// </remarks>
internal readonly record struct BitMask8
{
    public BitMask8(byte value) => Value = value;

    public byte Value { get; }

    public static BitMask8 Single(int bitIndex)
    {
        ValidateBitIndex(bitIndex);
        return new BitMask8((byte)(1 << bitIndex));
    }

    public static BitMask8 KeepHigh(int minimumBit)
    {
        ValidateBitIndex(minimumBit);
        return new BitMask8((byte)(0xFF << minimumBit));
    }

    public static BitMask8 KeepLow(int maximumBit)
    {
        ValidateBitIndex(maximumBit);
        return new BitMask8(maximumBit == 7 ? byte.MaxValue : (byte)((1 << (maximumBit + 1)) - 1));
    }

    public bool Contains(int bitIndex)
    {
        ValidateBitIndex(bitIndex);
        return (Value & (1 << bitIndex)) != 0;
    }

    public byte Apply(byte value) => (byte)(value & Value);

    public string ToBinaryString() => $"0b{Convert.ToString(Value, 2).PadLeft(8, '0')}";

    private static void ValidateBitIndex(int bitIndex)
    {
        if ((uint)bitIndex > 7u)
        {
            throw new ArgumentOutOfRangeException(nameof(bitIndex), bitIndex, "位索引必须位于 0–7。");
        }
    }
}

/// <summary>拥有一份连续且不可变的 8 位通道样本。</summary>
/// <remarks>
/// 构造时复制输入、读取时只暴露 ReadOnlyMemory，确保统计、投影和重建共享同一事实而不能互相改写。
/// Session 只缓存当前通道的一份 BytePlane，不预先分配五通道或八张完整位图。
/// </remarks>
internal sealed class BytePlane
{
    private readonly byte[] _values;

    public BytePlane(ImageSize size, BitPlaneChannel channel, ReadOnlySpan<byte> values)
    {
        if (values.Length != size.PixelCount)
        {
            throw new ArgumentException("通道样本数必须与图片像素数一致。", nameof(values));
        }

        Size = size;
        Channel = channel;
        _values = values.ToArray();
    }

    public ImageSize Size { get; }
    public BitPlaneChannel Channel { get; }
    public ReadOnlyMemory<byte> Values => _values;

    public byte this[int x, int y]
    {
        get
        {
            if ((uint)x >= (uint)Size.Width || (uint)y >= (uint)Size.Height)
            {
                throw new ArgumentOutOfRangeException(nameof(x), $"通道坐标 ({x},{y}) 超出 {Size.Width}×{Size.Height}。");
            }

            return _values[(y * Size.Width) + x];
        }
    }
}

/// <summary>一个位的确定性计数和信息量事实。</summary>
internal sealed record BitPlaneStatistics(
    int BitIndex,
    int Weight,
    long ZeroCount,
    long OneCount,
    double OneRatio,
    double BinaryEntropy);

/// <summary>像素探针返回的原始像素、通道、掩码和保留结果。</summary>
internal sealed record BitPlanePixelReport(
    int SourceX,
    int SourceY,
    byte Red,
    byte Green,
    byte Blue,
    byte Alpha,
    byte ChannelValue,
    string BinaryValue,
    byte Mask,
    byte KeptValue);

/// <summary>五通道重建结果；裁切数仅对 Y 重建有意义。</summary>
internal sealed record BitPlaneReconstructionResult(PixelImage Image, int ClippedPixelCount);

/// <summary>四个同坐标预览及其源坐标采样表。</summary>
internal sealed record BitPlaneProjection(
    PixelImage Source,
    PixelImage FocusedPlane,
    PixelImage CombinedPlane,
    PixelImage Reconstruction,
    BitPlanePreviewMap Coordinates,
    int ClippedPixelCount);

/// <summary>把有界预览像素稳定映射回原图坐标。</summary>
internal sealed class BitPlanePreviewMap
{
    private readonly int[] _sourceXs;
    private readonly int[] _sourceYs;

    private BitPlanePreviewMap(ImageSize sourceSize, ImageSize previewSize, int[] sourceXs, int[] sourceYs)
    {
        SourceSize = sourceSize;
        PreviewSize = previewSize;
        _sourceXs = sourceXs;
        _sourceYs = sourceYs;
    }

    public ImageSize SourceSize { get; }
    public ImageSize PreviewSize { get; }

    public static BitPlanePreviewMap Create(ImageSize sourceSize, int maximumEdge = 1024)
    {
        if (maximumEdge <= 0) throw new ArgumentOutOfRangeException(nameof(maximumEdge));
        var largest = Math.Max(sourceSize.Width, sourceSize.Height);
        var scale = largest <= maximumEdge ? 1d : maximumEdge / (double)largest;
        var width = Math.Max(1, (int)Math.Round(sourceSize.Width * scale));
        var height = Math.Max(1, (int)Math.Round(sourceSize.Height * scale));
        // 端点对齐的最近邻映射保证预览四角确实对应原图四角，同时保持所有预览共用完全相同的采样坐标。
        var xs = Enumerable.Range(0, width)
            .Select(x => width == 1 ? 0 : (int)Math.Round(x * (sourceSize.Width - 1d) / (width - 1d))).ToArray();
        var ys = Enumerable.Range(0, height)
            .Select(y => height == 1 ? 0 : (int)Math.Round(y * (sourceSize.Height - 1d) / (height - 1d))).ToArray();
        return new BitPlanePreviewMap(sourceSize, new ImageSize(width, height), xs, ys);
    }

    public (int X, int Y) GetSourcePoint(int previewX, int previewY)
    {
        if ((uint)previewX >= (uint)PreviewSize.Width || (uint)previewY >= (uint)PreviewSize.Height)
            throw new ArgumentOutOfRangeException(nameof(previewX), "预览坐标超出边界。");
        return (_sourceXs[previewX], _sourceYs[previewY]);
    }

    public (int X, int Y) FromNormalized(double x, double y) =>
        (Math.Clamp((int)(x * SourceSize.Width), 0, SourceSize.Width - 1),
         Math.Clamp((int)(y * SourceSize.Height), 0, SourceSize.Height - 1));
}
