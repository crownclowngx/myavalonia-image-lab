namespace ImageLabPlugin.Domain.Imaging;

/// <summary>负责六通道抽取，以及只替换选定通道的图片合成。</summary>
/// <remarks>
/// RGB 通道直接替换对应字节；Y/Cb/Cr 通道保留源像素另外两个分量，再使用与
/// <see cref="ColorSpaceConverter"/> 相同的全范围公式返回 RGB。Alpha 从不进入颜色运算。
/// </remarks>
internal sealed class ImageChannelConverter
{
    public ImageChannelPlane Extract(PixelImage image, ImageChannel channel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        var values = new double[checked((int)image.Size.PixelCount)];
        for (var y = 0; y < image.Size.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < image.Size.Width; x++)
            {
                var (red, green, blue, _) = image.GetPixel(x, y);
                values[(y * image.Size.Width) + x] = channel switch
                {
                    ImageChannel.Red => red,
                    ImageChannel.Green => green,
                    ImageChannel.Blue => blue,
                    ImageChannel.Luma => ColorSpaceConverter.ToLuma(red, green, blue),
                    ImageChannel.ChromaBlue => YCbCrColorSpace.FromRgb(red, green, blue).ChromaBlue,
                    ImageChannel.ChromaRed => YCbCrColorSpace.FromRgb(red, green, blue).ChromaRed,
                    _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "未知图片通道。")
                };
            }
        }

        return new ImageChannelPlane(image.Size, channel, values);
    }

    public ChannelReconstructionResult Apply(
        PixelImage source,
        ImageChannelPlane modified,
        MidpointRounding midpointRounding = MidpointRounding.ToEven)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(modified);
        if (source.Size != modified.Size)
        {
            throw new ArgumentException("重建通道尺寸与源图不一致。", nameof(modified));
        }

        var result = source.Clone();
        var clippedPixels = 0;
        var clippedComponents = 0;
        for (var y = 0; y < source.Size.Height; y++)
        {
            for (var x = 0; x < source.Size.Width; x++)
            {
                var (red, green, blue, _) = source.GetPixel(x, y);
                var value = modified[x, y];
                if (modified.Channel is ImageChannel.Red or ImageChannel.Green or ImageChannel.Blue)
                {
                    var channelByte = Clamp(value, midpointRounding, out var clipped);
                    clippedPixels += clipped ? 1 : 0;
                    clippedComponents += clipped ? 1 : 0;
                    result.SetRgb(
                        x,
                        y,
                        modified.Channel == ImageChannel.Red ? channelByte : red,
                        modified.Channel == ImageChannel.Green ? channelByte : green,
                        modified.Channel == ImageChannel.Blue ? channelByte : blue);
                    continue;
                }

                var original = YCbCrColorSpace.FromRgb(red, green, blue);
                var targetY = modified.Channel == ImageChannel.Luma ? value : original.Luma;
                var targetCb = modified.Channel == ImageChannel.ChromaBlue ? value : original.ChromaBlue;
                var targetCr = modified.Channel == ImageChannel.ChromaRed ? value : original.ChromaRed;
                var restored = YCbCrColorSpace.ToRgb(targetY, targetCb, targetCr);
                var r = Clamp(restored.Red, midpointRounding, out var redClipped);
                var g = Clamp(restored.Green, midpointRounding, out var greenClipped);
                var b = Clamp(restored.Blue, midpointRounding, out var blueClipped);
                clippedPixels += redClipped || greenClipped || blueClipped ? 1 : 0;
                clippedComponents += (redClipped ? 1 : 0) + (greenClipped ? 1 : 0) + (blueClipped ? 1 : 0);
                result.SetRgb(x, y, r, g, b);
            }
        }

        return new ChannelReconstructionResult(result, clippedPixels, clippedComponents);
    }

    public static double NeutralValue(ImageChannel channel) =>
        channel is ImageChannel.ChromaBlue or ImageChannel.ChromaRed ? 128d : 0d;

    private static byte Clamp(double value, MidpointRounding midpointRounding, out bool clipped)
    {
        clipped = value < 0d || value > 255d;
        return (byte)Math.Clamp((int)Math.Round(value, midpointRounding), 0, 255);
    }
}
