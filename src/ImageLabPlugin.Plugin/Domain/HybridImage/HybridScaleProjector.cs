using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Domain.HybridImage;

internal sealed record HybridScalePreview(int Divisor, HybridLumaPlane Raw, PixelImage Image);

/// <summary>从同一未量化 raw 平面生成 1×、1/2×、1/4×、1/8×真实观察尺度。</summary>
/// <remarks>
/// 面积覆盖平均发生在 double 域，之后才统一 ToEven 量化。若先缩放 byte Bitmap，第一次量化误差会被
/// 第二次平均放大，且不同 UI 渲染器可能产生不同结果，因此控件缩放不属于实验尺度。
/// </remarks>
internal sealed class HybridScaleProjector
{
    private static readonly int[] Divisors = [1, 2, 4, 8];

    public IReadOnlyList<HybridScalePreview> CreateAll(HybridLumaPlane source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Divisors.Select(divisor =>
        {
            var plane = divisor == 1 ? new HybridLumaPlane(source.Size, source.Values.Span) : Resize(source, divisor, cancellationToken);
            return new HybridScalePreview(divisor, plane, HybridImageComposer.Quantize(plane, cancellationToken));
        }).ToArray();
    }

    public HybridLumaPlane Resize(HybridLumaPlane source, int divisor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (divisor is not (2 or 4 or 8)) throw new ArgumentOutOfRangeException(nameof(divisor));
        var width = Math.Max(1, (int)Math.Ceiling(source.Size.Width / (double)divisor));
        var height = Math.Max(1, (int)Math.Ceiling(source.Size.Height / (double)divisor));
        var values = new double[checked(width * height)];
        for (var targetY = 0; targetY < height; targetY++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var top = targetY * divisor;
            var bottom = Math.Min(source.Size.Height, top + divisor);
            for (var targetX = 0; targetX < width; targetX++)
            {
                var left = targetX * divisor;
                var right = Math.Min(source.Size.Width, left + divisor);
                double sum = 0d;
                var count = 0;
                for (var y = top; y < bottom; y++)
                    for (var x = left; x < right; x++)
                    {
                        sum += source[x, y];
                        count++;
                    }
                values[(targetY * width) + targetX] = sum / count;
            }
        }
        return new HybridLumaPlane(new ImageSize(width, height), values);
    }

    /// <summary>为频谱诊断生成不超过给定最大边的有界 double 面积代理；小图仍返回独立平面。</summary>
    public HybridLumaPlane ResizeToMaximumEdge(HybridLumaPlane source, int maximumEdge,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (maximumEdge <= 0) throw new ArgumentOutOfRangeException(nameof(maximumEdge));
        if (Math.Max(source.Size.Width, source.Size.Height) <= maximumEdge)
            return new HybridLumaPlane(source.Size, source.Values.Span);
        var scale = maximumEdge / (double)Math.Max(source.Size.Width, source.Size.Height);
        var target = new ImageSize(Math.Max(1, (int)Math.Round(source.Size.Width * scale)),
            Math.Max(1, (int)Math.Round(source.Size.Height * scale)));
        return ResizeArea(source, target, cancellationToken);
    }

    private static HybridLumaPlane ResizeArea(HybridLumaPlane source, ImageSize target,
        CancellationToken cancellationToken)
    {
        var values = new double[checked((int)target.PixelCount)];
        var scaleX = source.Size.Width / (double)target.Width;
        var scaleY = source.Size.Height / (double)target.Height;
        for (var ty = 0; ty < target.Height; ty++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var top = ty * scaleY;
            var bottom = Math.Min(source.Size.Height, (ty + 1) * scaleY);
            for (var tx = 0; tx < target.Width; tx++)
            {
                var left = tx * scaleX;
                var right = Math.Min(source.Size.Width, (tx + 1) * scaleX);
                double sum = 0d, total = 0d;
                for (var sy = (int)Math.Floor(top); sy < Math.Ceiling(bottom); sy++)
                {
                    var wy = Math.Max(0d, Math.Min(bottom, sy + 1d) - Math.Max(top, sy));
                    for (var sx = (int)Math.Floor(left); sx < Math.Ceiling(right); sx++)
                    {
                        var wx = Math.Max(0d, Math.Min(right, sx + 1d) - Math.Max(left, sx));
                        var weight = wx * wy;
                        sum += source[Math.Min(sx, source.Size.Width - 1), Math.Min(sy, source.Size.Height - 1)] * weight;
                        total += weight;
                    }
                }
                values[(ty * target.Width) + tx] = sum / total;
            }
        }
        return new HybridLumaPlane(target, values);
    }
}
