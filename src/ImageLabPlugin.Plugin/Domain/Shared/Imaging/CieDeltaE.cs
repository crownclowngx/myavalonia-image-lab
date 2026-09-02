namespace ImageLabPlugin.Domain.Shared.Imaging;

/// <summary>只负责 CIELAB 色差；不依赖图片、调色板或 UI。</summary>
internal sealed class CieDeltaE
{
    public double DeltaE76(CieLabColor left, CieLabColor right)
    {
        Validate(left, nameof(left)); Validate(right, nameof(right));
        var dl = left.L - right.L; var da = left.A - right.A; var db = left.B - right.B;
        return Math.Sqrt((dl * dl) + (da * da) + (db * db));
    }

    /// <summary>
    /// 实现 Sharma/Wu/Dalal 的 CIEDE2000 公式（kL=kC=kH=1）。角度统一转为度处理 hue wrap，
    /// 最终三角函数再转弧度；零 chroma 分支不能用普通角度平均替代。
    /// </summary>
    public double Ciede2000(CieLabColor first, CieLabColor second)
    {
        Validate(first, nameof(first)); Validate(second, nameof(second));
        var c1 = Math.Sqrt((first.A * first.A) + (first.B * first.B));
        var c2 = Math.Sqrt((second.A * second.A) + (second.B * second.B));
        var cBar = (c1 + c2) / 2d;
        var c7 = Math.Pow(cBar, 7d);
        var g = 0.5d * (1d - Math.Sqrt(c7 / (c7 + Math.Pow(25d, 7d))));
        var a1p = (1d + g) * first.A; var a2p = (1d + g) * second.A;
        var c1p = Math.Sqrt((a1p * a1p) + (first.B * first.B));
        var c2p = Math.Sqrt((a2p * a2p) + (second.B * second.B));
        var h1p = Hue(a1p, first.B); var h2p = Hue(a2p, second.B);
        var dlp = second.L - first.L; var dcp = c2p - c1p;
        var dhp = c1p * c2p == 0d ? 0d : h2p - h1p;
        if (dhp > 180d) dhp -= 360d; else if (dhp < -180d) dhp += 360d;
        var dh = 2d * Math.Sqrt(c1p * c2p) * Math.Sin(ToRadians(dhp / 2d));
        var lBar = (first.L + second.L) / 2d; var cpBar = (c1p + c2p) / 2d;
        var hpBar = c1p * c2p == 0d ? h1p + h2p
            : Math.Abs(h1p - h2p) <= 180d ? (h1p + h2p) / 2d
            : h1p + h2p < 360d ? (h1p + h2p + 360d) / 2d : (h1p + h2p - 360d) / 2d;
        var t = 1d - (0.17d * Math.Cos(ToRadians(hpBar - 30d)))
            + (0.24d * Math.Cos(ToRadians(2d * hpBar)))
            + (0.32d * Math.Cos(ToRadians((3d * hpBar) + 6d)))
            - (0.20d * Math.Cos(ToRadians((4d * hpBar) - 63d)));
        var deltaTheta = 30d * Math.Exp(-Math.Pow((hpBar - 275d) / 25d, 2d));
        var cp7 = Math.Pow(cpBar, 7d);
        var rc = 2d * Math.Sqrt(cp7 / (cp7 + Math.Pow(25d, 7d)));
        var lTerm = lBar - 50d;
        var sl = 1d + ((0.015d * lTerm * lTerm) / Math.Sqrt(20d + (lTerm * lTerm)));
        var sc = 1d + (0.045d * cpBar); var sh = 1d + (0.015d * cpBar * t);
        var rt = -Math.Sin(ToRadians(2d * deltaTheta)) * rc;
        var l = dlp / sl; var c = dcp / sc; var h = dh / sh;
        return Math.Sqrt((l * l) + (c * c) + (h * h) + (rt * c * h));
    }

    private static double Hue(double a, double b)
    {
        if (a == 0d && b == 0d) return 0d;
        var angle = Math.Atan2(b, a) * 180d / Math.PI;
        return angle < 0d ? angle + 360d : angle;
    }
    private static double ToRadians(double degrees) => degrees * Math.PI / 180d;
    private static void Validate(CieLabColor color, string name)
    { if (!color.IsFinite) throw new ArgumentException("CIELAB 不能包含非有限数。", name); }
}
