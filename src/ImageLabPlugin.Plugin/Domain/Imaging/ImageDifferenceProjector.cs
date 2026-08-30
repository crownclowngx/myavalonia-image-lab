namespace ImageLabPlugin.Domain.Imaging;

/// <summary>生成用于人工检查的 RGB 绝对差异图；该投影不参与水印协议或质量判定。</summary>
internal static class ImageDifferenceProjector
{
    public static PixelImage Create(PixelImage original, PixelImage modified, int amplification = 4)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(modified);
        if (original.Size != modified.Size)
        {
            throw new ArgumentException("差异图要求两张图片尺寸一致。", nameof(modified));
        }

        if (amplification is < 1 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(amplification));
        }

        var first = original.Rgba.Span;
        var second = modified.Rgba.Span;
        var result = new byte[first.Length];
        for (var i = 0; i < result.Length; i += 4)
        {
            result[i] = Amplify(first[i], second[i], amplification);
            result[i + 1] = Amplify(first[i + 1], second[i + 1], amplification);
            result[i + 2] = Amplify(first[i + 2], second[i + 2], amplification);
            result[i + 3] = 255;
        }

        return new PixelImage(original.Size, result);
    }

    private static byte Amplify(byte first, byte second, int amplification) =>
        (byte)Math.Min(255, Math.Abs(first - second) * amplification);
}
