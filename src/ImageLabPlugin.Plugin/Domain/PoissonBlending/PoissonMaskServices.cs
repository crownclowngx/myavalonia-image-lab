using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Domain.PoissonBlending;

/// <summary>
/// 把闭开矩形和归一化画笔重放为二值像素域。点坐标按 <c>round(n×(length-1), ToEven)</c> 量化，
/// 圆盘使用像素中心的欧氏距离；线段以“点到线段距离不大于半径”填充，因此快速拖动不会留下断点。
/// </summary>
internal sealed class PoissonMaskRasterizer
{
    public const int MaximumStrokes = 512;

    public PoissonBinaryMask Rasterize(ImageSize size, PoissonMaskDefinition definition, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!string.Equals(definition.RasterProtocol, PoissonProtocols.Mask, StringComparison.Ordinal))
            throw new ArgumentException("遮罩栅格协议不受支持。", nameof(definition));
        if (definition.Strokes.Count > MaximumStrokes) throw new ArgumentOutOfRangeException(nameof(definition), "笔划数超过 512。 ");
        var mask = new PoissonBinaryMask(size);
        if (definition.Rectangle is { } rectangle)
        {
            rectangle.Validate(size);
            for (var y = rectangle.Top; y < rectangle.Bottom; y++)
                for (var x = rectangle.Left; x < rectangle.Right; x++) mask.Set(x, y, true);
        }

        foreach (var stroke in definition.Strokes.OrderBy(item => item.Sequence))
        {
            token.ThrowIfCancellationRequested(); stroke.Validate();
            var points = stroke.Points.Select(point => ToPixel(point, size)).ToArray();
            var radius = Math.Max(1d, stroke.RadiusNormalized * Math.Min(size.Width, size.Height));
            if (points.Length == 1) PaintSegment(mask, points[0], points[0], radius, stroke.Tool == PoissonMaskTool.Add, token);
            else for (var i = 1; i < points.Length; i++) PaintSegment(mask, points[i - 1], points[i], radius,
                stroke.Tool == PoissonMaskTool.Add, token);
        }
        return mask;
    }

    internal static (int X, int Y) ToPixel(PoissonNormalizedPoint point, ImageSize size)
    {
        point.Validate();
        return ((int)Math.Round(point.X * (size.Width - 1), MidpointRounding.ToEven),
            (int)Math.Round(point.Y * (size.Height - 1), MidpointRounding.ToEven));
    }

    private static void PaintSegment(PoissonBinaryMask mask, (int X, int Y) a, (int X, int Y) b,
        double radius, bool included, CancellationToken token)
    {
        var left = Math.Max(0, (int)Math.Floor(Math.Min(a.X, b.X) - radius));
        var right = Math.Min(mask.Size.Width - 1, (int)Math.Ceiling(Math.Max(a.X, b.X) + radius));
        var top = Math.Max(0, (int)Math.Floor(Math.Min(a.Y, b.Y) - radius));
        var bottom = Math.Min(mask.Size.Height - 1, (int)Math.Ceiling(Math.Max(a.Y, b.Y) + radius));
        var vx = b.X - a.X; var vy = b.Y - a.Y; var length2 = (double)((vx * vx) + (vy * vy));
        var radius2 = radius * radius;
        for (var y = top; y <= bottom; y++)
        {
            if ((y & 31) == 0) token.ThrowIfCancellationRequested();
            for (var x = left; x <= right; x++)
            {
                var t = length2 == 0d ? 0d : Math.Clamp((((x - a.X) * vx) + ((y - a.Y) * vy)) / length2, 0d, 1d);
                var dx = x - (a.X + (t * vx)); var dy = y - (a.Y + (t * vy));
                if ((dx * dx) + (dy * dy) <= radius2) mask.Set(x, y, included);
            }
        }
    }
}

/// <summary>计算 4 邻域连通分量、边界和由域外边框泛洪后剩余的孔洞；不会自动修改用户遮罩。</summary>
internal sealed class PoissonMaskTopologyAnalyzer
{
    private static readonly (int X, int Y)[] Directions = [(-1, 0), (1, 0), (0, -1), (0, 1)];

    public PoissonMaskTopology Analyze(PoissonBinaryMask mask, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(mask);
        var width = mask.Size.Width; var height = mask.Size.Height; var count = checked(width * height);
        var visited = new bool[count]; var unknown = 0; var components = 0; var boundary = 0;
        var minX = width; var minY = height; var maxX = -1; var maxY = -1;
        for (var y = 0; y < height; y++)
        {
            if ((y & 31) == 0) token.ThrowIfCancellationRequested();
            for (var x = 0; x < width; x++)
            {
                if (!mask.Contains(x, y)) continue;
                unknown++; minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
                if (Directions.Any(d => !IsInside(mask, x + d.X, y + d.Y))) boundary++;
                var index = (y * width) + x; if (visited[index]) continue;
                components++; Flood(mask, x, y, visited, seekIncluded: true, token);
            }
        }
        var holes = CountHoles(mask, token);
        var box = unknown == 0 ? new PoissonRectangle(0, 0, 0, 0) : new PoissonRectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
        return new PoissonMaskTopology(unknown, box, components, holes, boundary);
    }

    private static int CountHoles(PoissonBinaryMask mask, CancellationToken token)
    {
        var width = mask.Size.Width; var height = mask.Size.Height; var visited = new bool[checked(width * height)];
        for (var x = 0; x < width; x++) { Flood(mask, x, 0, visited, false, token); Flood(mask, x, height - 1, visited, false, token); }
        for (var y = 0; y < height; y++) { Flood(mask, 0, y, visited, false, token); Flood(mask, width - 1, y, visited, false, token); }
        var holes = 0;
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            var index = (y * width) + x;
            if (!mask.Contains(x, y) && !visited[index]) { holes++; Flood(mask, x, y, visited, false, token); }
        }
        return holes;
    }

    private static void Flood(PoissonBinaryMask mask, int startX, int startY, bool[] visited, bool seekIncluded, CancellationToken token)
    {
        if ((uint)startX >= (uint)mask.Size.Width || (uint)startY >= (uint)mask.Size.Height ||
            mask.Contains(startX, startY) != seekIncluded) return;
        var queue = new Queue<(int X, int Y)>(); queue.Enqueue((startX, startY));
        while (queue.Count > 0)
        {
            if ((queue.Count & 1023) == 0) token.ThrowIfCancellationRequested();
            var point = queue.Dequeue(); var index = (point.Y * mask.Size.Width) + point.X;
            if (visited[index] || mask.Contains(point.X, point.Y) != seekIncluded) continue;
            visited[index] = true;
            foreach (var direction in Directions)
            {
                var x = point.X + direction.X; var y = point.Y + direction.Y;
                if ((uint)x < (uint)mask.Size.Width && (uint)y < (uint)mask.Size.Height && !visited[(y * mask.Size.Width) + x]) queue.Enqueue((x, y));
            }
        }
    }

    private static bool IsInside(PoissonBinaryMask mask, int x, int y) =>
        (uint)x < (uint)mask.Size.Width && (uint)y < (uint)mask.Size.Height && mask.Contains(x, y);
}

/// <summary>
/// 在分配 RHS、邻接和解数组之前验证源/目标 1 像素 halo 及 Alpha。V1 只解决不透明 RGB Dirichlet 问题；
/// 对半透明像素返回结构化坐标，而不是擅自展平或求解隐藏 RGB。
/// </summary>
internal sealed class PoissonPlacementValidator
{
    private static readonly (int X, int Y)[] Halo = [(0, 0), (-1, 0), (1, 0), (0, -1), (0, 1)];

    public PoissonPlacementValidation Validate(PixelImage source, PixelImage target, PoissonBinaryMask mask,
        ImageOffset offset, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(mask);
        if (mask.Size != source.Size) throw new ArgumentException("遮罩尺寸必须等于源图尺寸。", nameof(mask));
        var issues = new List<PoissonPlacementIssue>();
        var any = false;
        for (var sy = 0; sy < source.Size.Height; sy++)
        {
            if ((sy & 31) == 0) token.ThrowIfCancellationRequested();
            for (var sx = 0; sx < source.Size.Width; sx++)
            {
                if (!mask.Contains(sx, sy)) continue; any = true;
                foreach (var d in Halo)
                {
                    var hx = sx + d.X; var hy = sy + d.Y; var tx = hx + offset.Dx; var ty = hy + offset.Dy;
                    if ((uint)hx >= (uint)source.Size.Width || (uint)hy >= (uint)source.Size.Height)
                    { AddOnce(issues, new("source-halo-out-of-bounds", "遮罩及其一像素源 halo 必须位于源图内部。", sx, sy)); continue; }
                    if ((uint)tx >= (uint)target.Size.Width || (uint)ty >= (uint)target.Size.Height)
                    { AddOnce(issues, new("target-halo-out-of-bounds", "映射区域及其一像素目标 halo 必须位于目标图内部。", sx, sy, tx, ty)); continue; }
                    var sourceAlpha = source.GetAlpha(hx, hy);
                    if (sourceAlpha != 255) AddOnce(issues, new("source-alpha-not-opaque", "V1 不支持源区域或 halo 的半透明像素；请先展平到不透明背景。", hx, hy, tx, ty, sourceAlpha));
                    var targetAlpha = target.GetAlpha(tx, ty);
                    if (targetAlpha != 255) AddOnce(issues, new("target-alpha-not-opaque", "V1 不支持目标区域或 halo 的半透明像素；请先展平到不透明背景。", hx, hy, tx, ty, targetAlpha));
                }
            }
        }
        if (!any) issues.Add(new("empty-mask", "遮罩至少需要包含一个像素。"));
        return new PoissonPlacementValidation(issues);
    }

    private static void AddOnce(List<PoissonPlacementIssue> issues, PoissonPlacementIssue issue)
    { if (issues.Count < 32 && !issues.Any(item => item.Code == issue.Code && item.SourceX == issue.SourceX && item.SourceY == issue.SourceY)) issues.Add(issue); }
}
