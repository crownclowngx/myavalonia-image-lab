namespace ImageLabPlugin.Domain.Shared.Imaging;

internal enum HueStatus { Defined, Undefined }

/// <summary>标准 HSV；H 为 [0,360) 角度，S/V 为 [0,1]，灰阶通过状态明确表达 Hue N/A。</summary>
internal readonly record struct HsvColor
{
    public HsvColor(double hueDegrees, double saturation, double value, HueStatus hueStatus)
    {
        if (!double.IsFinite(hueDegrees) || hueDegrees is < 0d or >= 360d)
            throw new ArgumentOutOfRangeException(nameof(hueDegrees));
        if (!double.IsFinite(saturation) || saturation is < 0d or > 1d)
            throw new ArgumentOutOfRangeException(nameof(saturation));
        if (!double.IsFinite(value) || value is < 0d or > 1d)
            throw new ArgumentOutOfRangeException(nameof(value));
        if (hueStatus == HueStatus.Undefined && hueDegrees != 0d)
            throw new ArgumentException("Hue 无定义时数值占位必须为 0；展示必须读取状态而不是把它解释成红色。");
        HueDegrees = hueDegrees;
        Saturation = saturation;
        Value = value;
        HueStatus = hueStatus;
    }

    public double HueDegrees { get; }
    public double Saturation { get; }
    public double Value { get; }
    public HueStatus HueStatus { get; }
}

/// <summary>标准 sRGB/HSV 转换，显式保存低色度 Hue 无定义语义。</summary>
internal sealed class HsvColorSpace
{
    public const double AchromaticEpsilon = 1e-12;

    public HsvColor ToHsv(SrgbColor color)
    {
        var max = Math.Max(color.Red, Math.Max(color.Green, color.Blue));
        var min = Math.Min(color.Red, Math.Min(color.Green, color.Blue));
        var delta = max - min;
        var saturation = max <= AchromaticEpsilon ? 0d : delta / max;
        if (delta < AchromaticEpsilon) return new HsvColor(0d, saturation, max, HueStatus.Undefined);
        var hue = max == color.Red
            ? 60d * (((color.Green - color.Blue) / delta) % 6d)
            : max == color.Green
                ? 60d * (((color.Blue - color.Red) / delta) + 2d)
                : 60d * (((color.Red - color.Green) / delta) + 4d);
        if (hue < 0d) hue += 360d;
        return new HsvColor(hue, saturation, max, HueStatus.Defined);
    }

    public SrgbColor FromHsv(HsvColor color)
    {
        if (color.HueStatus == HueStatus.Undefined)
            return new SrgbColor(color.Value, color.Value, color.Value);
        var chroma = color.Value * color.Saturation;
        var sector = color.HueDegrees / 60d;
        var x = chroma * (1d - Math.Abs((sector % 2d) - 1d));
        var (r, g, b) = sector switch
        {
            < 1d => (chroma, x, 0d),
            < 2d => (x, chroma, 0d),
            < 3d => (0d, chroma, x),
            < 4d => (0d, x, chroma),
            < 5d => (x, 0d, chroma),
            _ => (chroma, 0d, x)
        };
        var m = color.Value - chroma;
        return new SrgbColor(r + m, g + m, b + m);
    }
}
