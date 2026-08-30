using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ImageLabPlugin.Domain.FrequencyMaskEditing;

internal enum FrequencyMaskOperationKind
{
    BrushStroke,
    EraseStroke,
    RectangleFill,
    RingFill,
    InvertAll,
    ResetAllPass
}

/// <summary>中心化显示平面中的归一化坐标；与控件像素、DPI 和当前 FFT 尺寸无关。</summary>
internal readonly record struct NormalizedFrequencyPoint
{
    public NormalizedFrequencyPoint(double x, double y)
    {
        if (!double.IsFinite(x) || x is < 0d or > 1d || !double.IsFinite(y) || y is < 0d or > 1d)
            throw new ArgumentOutOfRangeException(nameof(x), "归一化频率坐标必须有限且位于 [0,1]²。");
        X = x;
        Y = y;
    }

    public double X { get; }
    public double Y { get; }
}

/// <summary>一次编辑固化的径向频带约束；重放旧操作时不读取 UI 当前开关。</summary>
internal readonly record struct FrequencyBandLock
{
    public FrequencyBandLock(double innerRadius, double outerRadius)
    {
        if (!double.IsFinite(innerRadius) || !double.IsFinite(outerRadius) || innerRadius < 0d || outerRadius > 1d || innerRadius >= outerRadius)
            throw new ArgumentOutOfRangeException(nameof(innerRadius), "频带锁定必须满足 0 ≤ inner < outer ≤ 1。");
        InnerRadius = innerRadius;
        OuterRadius = outerRadius;
    }

    public double InnerRadius { get; }
    public double OuterRadius { get; }
    public bool Contains(double radius) => radius >= InnerRadius && radius <= OuterRadius;
}

/// <summary>一条稳定、不可变且可确定性重放的遮罩编辑意图。</summary>
/// <remarks>
/// V1 用一个普通值对象和完整 switch 表达六种固定操作，不建立“一工具一接口”的层次。所有数组均防御性复制，
/// 因而历史和快照可以共享操作对象而不暴露可变状态。
/// </remarks>
internal sealed class FrequencyMaskOperation
{
    public const int MaximumStrokePoints = 4096;
    private readonly NormalizedFrequencyPoint[] _points;

    private FrequencyMaskOperation(FrequencyMaskOperationKind kind, ReadOnlySpan<NormalizedFrequencyPoint> points,
        NormalizedFrequencyPoint start, NormalizedFrequencyPoint end, double radius, double innerRadius,
        double outerRadius, double targetGain, double opacity, FrequencyBandLock? bandLock)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (!double.IsFinite(targetGain) || targetGain is < 0d or > 1d)
            throw new ArgumentOutOfRangeException(nameof(targetGain), "目标增益必须有限且位于 [0,1]。");
        if (!double.IsFinite(opacity) || opacity is <= 0d or > 1d)
            throw new ArgumentOutOfRangeException(nameof(opacity), "操作不透明度必须有限且位于 (0,1]。");
        if (points.Length > MaximumStrokePoints)
            throw new ArgumentOutOfRangeException(nameof(points), $"单条画笔最多保存 {MaximumStrokePoints} 个点。");

        Kind = kind;
        _points = points.ToArray();
        Start = start;
        End = end;
        Radius = radius;
        InnerRadius = innerRadius;
        OuterRadius = outerRadius;
        TargetGain = targetGain;
        Opacity = opacity;
        BandLock = bandLock;
    }

    public FrequencyMaskOperationKind Kind { get; }
    public IReadOnlyList<NormalizedFrequencyPoint> Points => Array.AsReadOnly(_points);
    internal ReadOnlySpan<NormalizedFrequencyPoint> PointSpan => _points;
    public NormalizedFrequencyPoint Start { get; }
    public NormalizedFrequencyPoint End { get; }
    public double Radius { get; }
    public double InnerRadius { get; }
    public double OuterRadius { get; }
    public double TargetGain { get; }
    public double Opacity { get; }
    public FrequencyBandLock? BandLock { get; }
    public int PointCount => _points.Length;

    public static FrequencyMaskOperation Brush(ReadOnlySpan<NormalizedFrequencyPoint> points, double radius,
        double targetGain, double opacity, FrequencyBandLock? bandLock = null)
    {
        ValidateStroke(points, radius);
        return new(FrequencyMaskOperationKind.BrushStroke, points, points[0], points[^1], radius, 0d, 0d,
            targetGain, opacity, bandLock);
    }

    public static FrequencyMaskOperation Eraser(ReadOnlySpan<NormalizedFrequencyPoint> points, double radius,
        double opacity, FrequencyBandLock? bandLock = null)
    {
        ValidateStroke(points, radius);
        return new(FrequencyMaskOperationKind.EraseStroke, points, points[0], points[^1], radius, 0d, 0d,
            1d, opacity, bandLock);
    }

    public static FrequencyMaskOperation Rectangle(NormalizedFrequencyPoint first, NormalizedFrequencyPoint second,
        double targetGain, double opacity, FrequencyBandLock? bandLock = null)
    {
        if (first.X == second.X || first.Y == second.Y)
            throw new ArgumentException("矩形的宽和高都必须大于零。", nameof(second));
        return new(FrequencyMaskOperationKind.RectangleFill, [], first, second, 0d, 0d, 0d,
            targetGain, opacity, bandLock);
    }

    public static FrequencyMaskOperation Ring(NormalizedFrequencyPoint center, double innerRadius, double outerRadius,
        double targetGain, double opacity, FrequencyBandLock? bandLock = null)
    {
        if (!double.IsFinite(innerRadius) || !double.IsFinite(outerRadius) || innerRadius < 0d || outerRadius > 1d || innerRadius >= outerRadius)
            throw new ArgumentOutOfRangeException(nameof(innerRadius), "圆环必须满足 0 ≤ inner < outer ≤ 1。");
        return new(FrequencyMaskOperationKind.RingFill, [], center, center, 0d, innerRadius, outerRadius,
            targetGain, opacity, bandLock);
    }

    public static FrequencyMaskOperation Invert() =>
        new(FrequencyMaskOperationKind.InvertAll, [], default, default, 0d, 0d, 0d, 0d, 1d, null);

    public static FrequencyMaskOperation Reset() =>
        new(FrequencyMaskOperationKind.ResetAllPass, [], default, default, 0d, 0d, 0d, 1d, 1d, null);

    internal void AppendCanonical(StringBuilder builder)
    {
        builder.Append((int)Kind).Append(':').Append(Radius.ToString("R", CultureInfo.InvariantCulture)).Append(':')
            .Append(InnerRadius.ToString("R", CultureInfo.InvariantCulture)).Append(':')
            .Append(OuterRadius.ToString("R", CultureInfo.InvariantCulture)).Append(':')
            .Append(TargetGain.ToString("R", CultureInfo.InvariantCulture)).Append(':')
            .Append(Opacity.ToString("R", CultureInfo.InvariantCulture)).Append(':')
            .Append(Start.X.ToString("R", CultureInfo.InvariantCulture)).Append(',')
            .Append(Start.Y.ToString("R", CultureInfo.InvariantCulture)).Append(':')
            .Append(End.X.ToString("R", CultureInfo.InvariantCulture)).Append(',')
            .Append(End.Y.ToString("R", CultureInfo.InvariantCulture)).Append(':');
        if (BandLock is { } band)
            builder.Append(band.InnerRadius.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(band.OuterRadius.ToString("R", CultureInfo.InvariantCulture));
        builder.Append(':');
        foreach (var point in _points)
            builder.Append(point.X.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(point.Y.ToString("R", CultureInfo.InvariantCulture)).Append(';');
        builder.Append('|');
    }

    private static void ValidateStroke(ReadOnlySpan<NormalizedFrequencyPoint> points, double radius)
    {
        if (points.IsEmpty) throw new ArgumentException("画笔路径至少需要一个点。", nameof(points));
        if (points.Length > MaximumStrokePoints) throw new ArgumentOutOfRangeException(nameof(points));
        if (!double.IsFinite(radius) || radius is <= 0d or > 1d)
            throw new ArgumentOutOfRangeException(nameof(radius), "画笔半径必须有限且位于 (0,1]。");
    }
}

/// <summary>全通基线、全局强度和有序编辑操作组成的不可变配方。</summary>
internal sealed class FrequencyMaskRecipe
{
    public const int MaximumOperations = 128;
    public const int MaximumTotalPoints = 32768;
    private readonly FrequencyMaskOperation[] _operations;

    public FrequencyMaskRecipe(double strength, IEnumerable<FrequencyMaskOperation>? operations = null,
        int? originalPaddedWidth = null, int? originalPaddedHeight = null)
    {
        if (!double.IsFinite(strength) || strength is < 0d or > 1d)
            throw new ArgumentOutOfRangeException(nameof(strength), "全局遮罩强度必须有限且位于 [0,1]。");
        _operations = operations?.ToArray() ?? [];
        if (_operations.Length > MaximumOperations)
            throw new ArgumentOutOfRangeException(nameof(operations), $"配方最多包含 {MaximumOperations} 条操作。");
        if (_operations.Any(static operation => operation is null)) throw new ArgumentException("操作不能为空。", nameof(operations));
        if (_operations.Sum(static operation => operation.PointCount) > MaximumTotalPoints)
            throw new ArgumentOutOfRangeException(nameof(operations), $"配方最多包含 {MaximumTotalPoints} 个画笔采样点。");
        if ((originalPaddedWidth is null) != (originalPaddedHeight is null) ||
            originalPaddedWidth is <= 0 or > 2048 || originalPaddedHeight is <= 0 or > 2048)
            throw new ArgumentOutOfRangeException(nameof(originalPaddedWidth), "原始 padded 尺寸必须成对提供且位于 1..2048。");
        Strength = strength;
        OriginalPaddedWidth = originalPaddedWidth;
        OriginalPaddedHeight = originalPaddedHeight;
    }

    public double Strength { get; }
    public IReadOnlyList<FrequencyMaskOperation> Operations => Array.AsReadOnly(_operations);
    internal ReadOnlySpan<FrequencyMaskOperation> OperationSpan => _operations;
    public int? OriginalPaddedWidth { get; }
    public int? OriginalPaddedHeight { get; }

    public FrequencyMaskRecipe WithStrength(double strength) =>
        new(strength, _operations, OriginalPaddedWidth, OriginalPaddedHeight);

    public string Fingerprint()
    {
        var builder = new StringBuilder("frequency-mask-recipe-v1|")
            .Append(Strength.ToString("R", CultureInfo.InvariantCulture)).Append('|')
            .Append(OriginalPaddedWidth?.ToString(CultureInfo.InvariantCulture) ?? "-").Append('|')
            .Append(OriginalPaddedHeight?.ToString(CultureInfo.InvariantCulture) ?? "-").Append('|');
        foreach (var operation in _operations) operation.AppendCanonical(builder);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))[..16].ToLowerInvariant();
    }
}
