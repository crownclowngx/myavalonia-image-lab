using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Domain.HybridImage;

/// <summary>使用中心化闭式最小二乘求解固定方向 B→A 的无镜像相似变换。</summary>
/// <remarks>
/// 中心化后，平移从质心直接恢复；旋转由二维协方差的 dot/cross 两个标量得到，缩放由投影长度得到。
/// 这种实现没有迭代、通用矩阵库或隐藏 tie-break，输入顺序也不会改变数学结果。
/// </remarks>
internal sealed class SimilarityTransformSolver
{
    private const double MinimumBaselineRatio = 0.02d;

    public HybridAlignmentSolution Solve(
        IReadOnlyList<HybridAlignmentPointPair> pairs,
        ImageSize sizeA,
        ImageSize sizeB)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        if (pairs.Count is < 2 or > 8) throw new ArgumentOutOfRangeException(nameof(pairs), "控制点必须为 2–8 对。");
        if (pairs.Select(static pair => pair.Id).Distinct().Count() != pairs.Count)
            throw new ArgumentException("控制点编号不能重复。", nameof(pairs));

        var a = pairs.Select(pair => pair.PointA.ToPixelCenter(sizeA)).ToArray();
        var b = pairs.Select(pair => pair.PointB.ToPixelCenter(sizeB)).ToArray();
        var minimumBaseline = Math.Min(FindMinimumBaselineRatio(a, sizeA), FindMinimumBaselineRatio(b, sizeB));
        if (minimumBaseline < MinimumBaselineRatio)
            throw new InvalidOperationException("控制点基线短于图片对角线的 2%，相似变换对噪声过于敏感。");

        var centerA = Center(a);
        var centerB = Center(b);
        double dot = 0d, cross = 0d, reflectionDot = 0d, reflectionCross = 0d, denominator = 0d;
        for (var i = 0; i < pairs.Count; i++)
        {
            var ax = a[i].X - centerA.X;
            var ay = a[i].Y - centerA.Y;
            var bx = b[i].X - centerB.X;
            var by = b[i].Y - centerB.Y;
            dot += (bx * ax) + (by * ay);
            cross += (bx * ay) - (by * ax);
            reflectionDot += (bx * ax) - (by * ay);
            reflectionCross += (bx * ay) + (by * ax);
            denominator += (bx * bx) + (by * by);
        }

        if (denominator <= 1e-12) throw new InvalidOperationException("B 控制点退化，无法求解缩放与旋转。");
        var directMagnitude = Math.Sqrt((dot * dot) + (cross * cross));
        var reflectionMagnitude = Math.Sqrt((reflectionDot * reflectionDot) + (reflectionCross * reflectionCross));
        if (reflectionMagnitude > directMagnitude * (1d + 1e-10))
            throw new InvalidOperationException("控制点更符合镜像关系；V1 禁止镜像对齐。");
        if (directMagnitude <= 1e-12) throw new InvalidOperationException("控制点协方差接近零，无法确定旋转。");

        var scale = directMagnitude / denominator;
        if (!double.IsFinite(scale) || scale is < 0.1d or > 10d)
            throw new InvalidOperationException("求得的统一缩放超出 [0.1,10] 安全范围。");
        var rotation = Math.Atan2(cross, dot);
        var cosine = Math.Cos(rotation);
        var sine = Math.Sin(rotation);
        var tx = centerA.X - scale * ((cosine * centerB.X) - (sine * centerB.Y));
        var ty = centerA.Y - scale * ((sine * centerB.X) + (cosine * centerB.Y));
        var transform = new HybridSimilarityTransform(scale, rotation, tx, ty);

        double squared = 0d, maximum = 0d;
        for (var i = 0; i < pairs.Count; i++)
        {
            var mapped = transform.MapBToA(b[i]);
            var residual = Math.Sqrt(Math.Pow(mapped.X - a[i].X, 2d) + Math.Pow(mapped.Y - a[i].Y, 2d));
            squared += residual * residual;
            maximum = Math.Max(maximum, residual);
        }
        var rms = Math.Sqrt(squared / pairs.Count);
        var diagonal = Math.Sqrt((sizeA.Width * (double)sizeA.Width) + (sizeA.Height * (double)sizeA.Height));
        return new HybridAlignmentSolution(transform,
            pairs.Count == 2 ? HybridResidualStatus.NotIndependentlyValidated : HybridResidualStatus.Measured,
            rms, maximum, rms / diagonal, minimumBaseline);
    }

    private static HybridPoint Center(IReadOnlyList<HybridPoint> points) =>
        new(points.Average(static point => point.X), points.Average(static point => point.Y));

    private static double FindMinimumBaselineRatio(IReadOnlyList<HybridPoint> points, ImageSize size)
    {
        var minimum = double.PositiveInfinity;
        for (var i = 0; i < points.Count - 1; i++)
            for (var j = i + 1; j < points.Count; j++)
                minimum = Math.Min(minimum, Math.Sqrt(Math.Pow(points[i].X - points[j].X, 2d) +
                    Math.Pow(points[i].Y - points[j].Y, 2d)));
        var diagonal = Math.Sqrt((size.Width * (double)size.Width) + (size.Height * (double)size.Height));
        return minimum / diagonal;
    }
}
