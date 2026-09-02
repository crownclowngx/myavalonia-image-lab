namespace ImageLabPlugin.Domain.ImageOscilloscope;

/// <summary>实现图像示波器唯一的白底合成、BT.601 色度和 HSV 数值协议。</summary>
/// <remarks>
/// 这里处理的是 gamma-coded sRGB 字节，不是线性光。先用 ToEven 把透明像素合成到白底，再由同一份
/// 可见 RGB 计算 Y/Cb/Cr/HSV，确保 Waveform、Vectorscope、裁切与探针不会出现公式分叉。
/// </remarks>
internal sealed class OscilloscopeColorConverter
{
    private const double HueEpsilon = 1e-12;

    public OscilloscopePixel Convert(byte red, byte green, byte blue, byte alpha)
    {
        var visibleRed = CompositeOnWhite(red, alpha);
        var visibleGreen = CompositeOnWhite(green, alpha);
        var visibleBlue = CompositeOnWhite(blue, alpha);
        var y = (0.299d * visibleRed) + (0.587d * visibleGreen) + (0.114d * visibleBlue);
        var luma = QuantizeByte(y);
        var cb = Math.Clamp((visibleBlue - y) / (1.772d * 255d), -0.5d, 0.5d);
        var cr = Math.Clamp((visibleRed - y) / (1.402d * 255d), -0.5d, 0.5d);
        var (saturation, hue) = ToHsv(visibleRed, visibleGreen, visibleBlue);
        return new OscilloscopePixel(visibleRed, visibleGreen, visibleBlue, alpha, luma, cb, cr, saturation, hue);
    }

    public static byte CompositeOnWhite(byte channel, byte alpha)
    {
        var value = ((alpha * channel) + ((255 - alpha) * 255d)) / 255d;
        return QuantizeByte(value);
    }

    internal static byte QuantizeByte(double value)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value), "量化值必须是有限数。 ");
        return (byte)Math.Clamp((int)Math.Round(value, MidpointRounding.ToEven), 0, 255);
    }

    private static (double Saturation, double? Hue) ToHsv(byte red, byte green, byte blue)
    {
        var r = red / 255d;
        var g = green / 255d;
        var b = blue / 255d;
        var maximum = Math.Max(r, Math.Max(g, b));
        var minimum = Math.Min(r, Math.Min(g, b));
        var delta = maximum - minimum;
        var saturation = maximum <= 0d ? 0d : delta / maximum;
        if (delta <= HueEpsilon || saturation <= HueEpsilon) return (saturation, null);

        double hue;
        if (maximum == r) hue = 60d * (((g - b) / delta) % 6d);
        else if (maximum == g) hue = 60d * (((b - r) / delta) + 2d);
        else hue = 60d * (((r - g) / delta) + 4d);
        if (hue < 0d) hue += 360d;
        return (saturation, hue >= 360d ? 0d : hue);
    }
}
