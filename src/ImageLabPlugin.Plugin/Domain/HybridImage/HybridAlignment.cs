using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Domain.HybridImage;

/// <summary>图像内部的归一化坐标；左上角为 (0,0)，右下角为 (1,1)。</summary>
/// <remarks>
/// 归一化坐标是配方的稳定事实，不依赖代理或原图尺寸。投影到像素时把 0/1 映射到首尾像素中心，
/// 因而同一组控制点可以在代理与完整尺寸之间复用，而不会累积整数舍入误差。
/// </remarks>
internal readonly record struct HybridNormalizedPoint
{
    public HybridNormalizedPoint(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || x is < 0d or > 1d || y is < 0d or > 1d)
            throw new ArgumentOutOfRangeException(nameof(x), "归一化坐标必须是 [0,1] 内的有限值。");
        X = x;
        Y = y;
    }

    public double X { get; }
    public double Y { get; }

    public HybridPoint ToPixelCenter(ImageSize size) => new(
        X * Math.Max(0, size.Width - 1) + 0.5d,
        Y * Math.Max(0, size.Height - 1) + 0.5d);
}

internal readonly record struct HybridPoint(double X, double Y)
{
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);
}

/// <summary>一对不可拆分的 A/B 对应点；A 永远是低频参考坐标系，B 永远是待变换输入。</summary>
internal sealed record HybridAlignmentPointPair
{
    public HybridAlignmentPointPair(int id, HybridNormalizedPoint pointA, HybridNormalizedPoint pointB)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id), "控制点编号必须为正数。");
        Id = id;
        PointA = pointA;
        PointB = pointB;
    }

    public int Id { get; }
    public HybridNormalizedPoint PointA { get; }
    public HybridNormalizedPoint PointB { get; }
}

internal enum HybridResidualStatus
{
    NotIndependentlyValidated,
    Measured
}

/// <summary>无镜像二维相似变换，方向固定为 B→A。</summary>
/// <remarks>
/// 正向公式为 A=sR×B+t。逆变换使用解析式 Rᵀ/s，不在逐像素采样中重复求逆，
/// 既减少热路径成本，也让正逆 round-trip 的数值语义只有一处。
/// </remarks>
internal readonly record struct HybridSimilarityTransform
{
    public HybridSimilarityTransform(double scale, double rotationRadians, double translateX, double translateY)
    {
        if (!double.IsFinite(scale) || scale <= 0d || !double.IsFinite(rotationRadians) ||
            !double.IsFinite(translateX) || !double.IsFinite(translateY))
            throw new ArgumentOutOfRangeException(nameof(scale), "相似变换参数必须有限且缩放为正数。");
        Scale = scale;
        RotationRadians = NormalizeRadians(rotationRadians);
        TranslateX = translateX;
        TranslateY = translateY;
    }

    public double Scale { get; }
    public double RotationRadians { get; }
    public double RotationDegrees => RotationRadians * 180d / Math.PI;
    public double TranslateX { get; }
    public double TranslateY { get; }

    public HybridPoint MapBToA(HybridPoint point)
    {
        var cosine = Math.Cos(RotationRadians);
        var sine = Math.Sin(RotationRadians);
        return new HybridPoint(
            Scale * ((cosine * point.X) - (sine * point.Y)) + TranslateX,
            Scale * ((sine * point.X) + (cosine * point.Y)) + TranslateY);
    }

    public HybridPoint MapAToB(HybridPoint point)
    {
        var x = point.X - TranslateX;
        var y = point.Y - TranslateY;
        var cosine = Math.Cos(RotationRadians);
        var sine = Math.Sin(RotationRadians);
        return new HybridPoint(
            ((cosine * x) + (sine * y)) / Scale,
            ((-sine * x) + (cosine * y)) / Scale);
    }

    private static double NormalizeRadians(double value)
    {
        var normalized = (value + Math.PI) % (2d * Math.PI);
        if (normalized < 0d) normalized += 2d * Math.PI;
        return normalized - Math.PI;
    }
}

internal sealed record HybridAlignmentSolution(
    HybridSimilarityTransform Transform,
    HybridResidualStatus ResidualStatus,
    double RmsResidualPixels,
    double MaximumResidualPixels,
    double NormalizedRmsResidual,
    double MinimumBaselineRatio);

/// <summary>左闭右开的整数裁切矩形。</summary>
internal readonly record struct HybridCropRectangle
{
    public HybridCropRectangle(int x, int y, int width, int height)
    {
        if (x < 0 || y < 0 || width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(x), "裁切矩形必须位于非负坐标且尺寸为正数。");
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }
    public int Right => checked(X + Width);
    public int Bottom => checked(Y + Height);
    public long Area => checked((long)Width * Height);
    public ImageSize Size => new(Width, Height);

    public bool IsInside(ImageSize size) => Right <= size.Width && Bottom <= size.Height;
}

/// <summary>配方中与代理尺寸无关的归一化裁切矩形。</summary>
internal readonly record struct HybridNormalizedCrop
{
    public HybridNormalizedCrop(double left, double top, double right, double bottom)
    {
        if (!double.IsFinite(left) || !double.IsFinite(top) || !double.IsFinite(right) || !double.IsFinite(bottom) ||
            left < 0d || top < 0d || right > 1d || bottom > 1d || right <= left || bottom <= top)
            throw new ArgumentOutOfRangeException(nameof(left), "归一化裁切必须是 [0,1] 内非空的左闭右开矩形。");
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    public double Left { get; }
    public double Top { get; }
    public double Right { get; }
    public double Bottom { get; }

    public HybridCropRectangle ToPixels(ImageSize size)
    {
        var left = Math.Clamp((int)Math.Floor(Left * size.Width), 0, size.Width - 1);
        var top = Math.Clamp((int)Math.Floor(Top * size.Height), 0, size.Height - 1);
        var right = Math.Clamp((int)Math.Ceiling(Right * size.Width), left + 1, size.Width);
        var bottom = Math.Clamp((int)Math.Ceiling(Bottom * size.Height), top + 1, size.Height);
        return new HybridCropRectangle(left, top, right - left, bottom - top);
    }

    public static HybridNormalizedCrop FromPixels(HybridCropRectangle crop, ImageSize size)
    {
        if (!crop.IsInside(size)) throw new ArgumentException("裁切矩形超出参考图片。", nameof(crop));
        return new HybridNormalizedCrop(crop.X / (double)size.Width, crop.Y / (double)size.Height,
            crop.Right / (double)size.Width, crop.Bottom / (double)size.Height);
    }
}

/// <summary>Hybrid Image V1 的完整数学身份。</summary>
/// <remarks>文件路径、代理尺寸和运行时间不影响算法结果，因此都不进入指纹。</remarks>
internal sealed record HybridImageRecipe
{
    public const double MinimumSigma = 0.8d;
    public const double MaximumSigma = 32d;
    public const double MaximumGain = 2d;

    public HybridImageRecipe(IReadOnlyList<HybridAlignmentPointPair> points, HybridNormalizedCrop crop,
        double lowSigmaPixels = 8d, double highSigmaPixels = 6d, double lowGain = 1d, double highGain = 1d)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count is < 2 or > 8) throw new ArgumentOutOfRangeException(nameof(points), "控制点必须为 2–8 对。");
        if (points.Select(static point => point.Id).Distinct().Count() != points.Count)
            throw new ArgumentException("控制点编号不能重复。", nameof(points));
        ValidateRange(lowSigmaPixels, MinimumSigma, MaximumSigma, nameof(lowSigmaPixels));
        ValidateRange(highSigmaPixels, MinimumSigma, MaximumSigma, nameof(highSigmaPixels));
        ValidateRange(lowGain, 0d, MaximumGain, nameof(lowGain));
        ValidateRange(highGain, 0d, MaximumGain, nameof(highGain));
        Points = points.OrderBy(static point => point.Id).ToArray();
        Crop = crop;
        LowSigmaPixels = lowSigmaPixels;
        HighSigmaPixels = highSigmaPixels;
        LowGain = lowGain;
        HighGain = highGain;
    }

    public IReadOnlyList<HybridAlignmentPointPair> Points { get; }
    public HybridNormalizedCrop Crop { get; }
    public double LowSigmaPixels { get; }
    public double HighSigmaPixels { get; }
    public double LowGain { get; }
    public double HighGain { get; }

    public string Fingerprint()
    {
        var builder = new StringBuilder("hybrid-image-v1|gray-white|gaussian-reflect101|bilinear-toeven");
        foreach (var point in Points)
            builder.Append('|').Append(point.Id).Append(':').Append(Format(point.PointA.X)).Append(',')
                .Append(Format(point.PointA.Y)).Append('>').Append(Format(point.PointB.X)).Append(',').Append(Format(point.PointB.Y));
        builder.Append('|').Append(Format(Crop.Left)).Append(',').Append(Format(Crop.Top)).Append(',')
            .Append(Format(Crop.Right)).Append(',').Append(Format(Crop.Bottom)).Append('|')
            .Append(Format(LowSigmaPixels)).Append('|').Append(Format(HighSigmaPixels)).Append('|')
            .Append(Format(LowGain)).Append('|').Append(Format(HighGain));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))[..24].ToLowerInvariant();
    }

    private static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static void ValidateRange(double value, double minimum, double maximum, string name)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
            throw new ArgumentOutOfRangeException(name, $"{name} 必须是 [{minimum},{maximum}] 内的有限值。");
    }
}

/// <summary>拥有一份不可从外部修改的 double 亮度平面，规范范围由具体处理阶段解释。</summary>
internal sealed class HybridLumaPlane
{
    private readonly double[] _values;

    public HybridLumaPlane(ImageSize size, ReadOnlySpan<double> values)
    {
        if (values.Length != size.PixelCount) throw new ArgumentException("亮度样本数与尺寸不一致。", nameof(values));
        foreach (var value in values)
            if (!double.IsFinite(value)) throw new ArgumentException("亮度平面包含非有限值。", nameof(values));
        Size = size;
        _values = values.ToArray();
    }

    public ImageSize Size { get; }
    public ReadOnlyMemory<double> Values => _values;
    public double this[int x, int y] => _values[checked((y * Size.Width) + x)];
}
