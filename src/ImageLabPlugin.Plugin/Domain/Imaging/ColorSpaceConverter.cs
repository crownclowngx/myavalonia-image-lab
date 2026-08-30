namespace ImageLabPlugin.Domain.Imaging;

/// <summary>提供不依赖 UI 或图片编解码器的 RGB/YCbCr 亮度转换。</summary>
internal static class ColorSpaceConverter
{
    public static LumaPlane ExtractLuma(PixelImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var values = new double[checked((int)image.Size.PixelCount)];
        for (var y = 0; y < image.Size.Height; y++)
        {
            for (var x = 0; x < image.Size.Width; x++)
            {
                var (red, green, blue, _) = image.GetPixel(x, y);
                values[(y * image.Size.Width) + x] = ToLuma(red, green, blue);
            }
        }

        return new LumaPlane(image.Size, values);
    }

    /// <summary>把修改后的 Y 与源图的 Cb/Cr 组合回 RGB，并保持 Alpha 完全不变。</summary>
    public static PixelImage ApplyLuma(PixelImage source, LumaPlane modifiedLuma)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(modifiedLuma);
        if (source.Size != modifiedLuma.Size)
        {
            throw new ArgumentException("亮度平面尺寸与源图不一致。", nameof(modifiedLuma));
        }

        var result = source.Clone();
        for (var y = 0; y < source.Size.Height; y++)
        {
            for (var x = 0; x < source.Size.Width; x++)
            {
                var (red, green, blue, _) = source.GetPixel(x, y);
                var originalY = ToLuma(red, green, blue);
                var original = YCbCrColorSpace.FromRgb(red, green, blue);
                var targetY = modifiedLuma[x, y];
                if (Math.Abs(targetY - originalY) < 1e-9)
                {
                    // 未参与任何频域块的像素必须保持逐字节不变，尤其是透明块和非 8 倍数边缘。
                    continue;
                }

                // 用完整 YCbCr 逆变换而不是简单给 RGB 同加一个偏移，可以让颜色变化更可控。
                var restored = YCbCrColorSpace.ToRgb(targetY, original.ChromaBlue, original.ChromaRed);

                result.SetRgb(x, y, Clamp(restored.Red), Clamp(restored.Green), Clamp(restored.Blue));
            }
        }

        return result;
    }

    public static double ToLuma(byte red, byte green, byte blue) =>
        YCbCrColorSpace.FromRgb(red, green, blue).Luma;

    private static byte Clamp(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);
}
