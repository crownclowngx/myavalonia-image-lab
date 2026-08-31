namespace ImageLabPlugin.Domain.Imaging;

/// <summary>CIELAB D65 颜色；L* 通常位于 [0,100]，a*/b* 在中间计算中不预先裁切。</summary>
internal readonly record struct CieLabColor(double L, double A, double B)
{
    public bool IsFinite => double.IsFinite(L) && double.IsFinite(A) && double.IsFinite(B);
}

/// <summary>在固定 D65 白点下转换 XYZ 与 CIELAB。</summary>
/// <remarks>
/// V1 的所有输入和输出均为 sRGB D65，因此不做 Bradford 白点适配。δ=6/29 的分段保证黑场附近连续，
/// 若把公式简化为无条件立方根，会在暗部产生错误的 L*。
/// </remarks>
internal sealed class CieLabColorSpace
{
    public const double WhiteX = 0.95047d;
    public const double WhiteY = 1d;
    public const double WhiteZ = 1.08883d;
    private const double Delta = 6d / 29d;
    private const double DeltaSquared = Delta * Delta;
    private const double DeltaCubed = DeltaSquared * Delta;

    public CieLabColor ToLab(XyzD65Color xyz)
    {
        if (!xyz.IsFinite) throw new ArgumentException("XYZ 不能包含非有限数。", nameof(xyz));
        var fx = Forward(xyz.X / WhiteX);
        var fy = Forward(xyz.Y / WhiteY);
        var fz = Forward(xyz.Z / WhiteZ);
        return new CieLabColor((116d * fy) - 16d, 500d * (fx - fy), 200d * (fy - fz));
    }

    public XyzD65Color FromLab(CieLabColor lab)
    {
        if (!lab.IsFinite) throw new ArgumentException("CIELAB 不能包含非有限数。", nameof(lab));
        var fy = (lab.L + 16d) / 116d;
        var fx = fy + (lab.A / 500d);
        var fz = fy - (lab.B / 200d);
        return new XyzD65Color(WhiteX * Reverse(fx), WhiteY * Reverse(fy), WhiteZ * Reverse(fz));
    }

    private static double Forward(double value) => value > DeltaCubed
        ? Math.Cbrt(value)
        : (value / (3d * DeltaSquared)) + (4d / 29d);

    private static double Reverse(double value) => value > Delta
        ? value * value * value
        : 3d * DeltaSquared * (value - (4d / 29d));
}
