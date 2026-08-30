using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Domain.Fingerprinting;

/// <summary>将完整 RGBA 图片归一化为确定尺寸的视觉亮度矩阵。</summary>
/// <remarks>
/// RGBA 先按未预乘语义合成到固定白底，再使用 BT.601 全范围亮度。缩小时按目标像素覆盖源像素的面积加权；
/// 任一维需要放大时使用像素中心对齐双线性插值。矩阵全程保留 double，避免展示舍入污染算法摘要。
/// </remarks>
internal sealed class FingerprintLumaNormalizer
{
    public const string NormalizationId = "fingerprint-luma-bt601-white-matte-area-bilinear-v1";

    public double[] Normalize(PixelImage source, int width, int height, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "目标尺寸必须大于零。");
        return source.Size.Width >= width && source.Size.Height >= height
            ? DownsampleByArea(source, width, height, cancellationToken)
            : ResizeBilinear(source, width, height, cancellationToken);
    }

    private static double[] DownsampleByArea(PixelImage source, int width, int height, CancellationToken token)
    {
        var result = new double[checked(width * height)];
        var scaleX = source.Size.Width / (double)width;
        var scaleY = source.Size.Height / (double)height;
        for (var targetY = 0; targetY < height; targetY++)
        {
            token.ThrowIfCancellationRequested();
            var top = targetY * scaleY;
            var bottom = Math.Min(source.Size.Height, (targetY + 1) * scaleY);
            for (var targetX = 0; targetX < width; targetX++)
            {
                var left = targetX * scaleX;
                var right = Math.Min(source.Size.Width, (targetX + 1) * scaleX);
                double weighted = 0d, totalWeight = 0d;
                for (var sourceY = (int)Math.Floor(top); sourceY < Math.Ceiling(bottom); sourceY++)
                {
                    var yWeight = Math.Max(0d, Math.Min(bottom, sourceY + 1d) - Math.Max(top, sourceY));
                    for (var sourceX = (int)Math.Floor(left); sourceX < Math.Ceiling(right); sourceX++)
                    {
                        var xWeight = Math.Max(0d, Math.Min(right, sourceX + 1d) - Math.Max(left, sourceX));
                        var weight = xWeight * yWeight;
                        weighted += VisualLuma(source, Math.Min(sourceX, source.Size.Width - 1), Math.Min(sourceY, source.Size.Height - 1)) * weight;
                        totalWeight += weight;
                    }
                }
                result[(targetY * width) + targetX] = weighted / totalWeight;
            }
        }
        return result;
    }

    private static double[] ResizeBilinear(PixelImage source, int width, int height, CancellationToken token)
    {
        var result = new double[checked(width * height)];
        for (var y = 0; y < height; y++)
        {
            token.ThrowIfCancellationRequested();
            var sourceY = ((y + 0.5d) * source.Size.Height / height) - 0.5d;
            var y0 = Math.Clamp((int)Math.Floor(sourceY), 0, source.Size.Height - 1);
            var y1 = Math.Min(y0 + 1, source.Size.Height - 1);
            var fy = Math.Clamp(sourceY - y0, 0d, 1d);
            for (var x = 0; x < width; x++)
            {
                var sourceX = ((x + 0.5d) * source.Size.Width / width) - 0.5d;
                var x0 = Math.Clamp((int)Math.Floor(sourceX), 0, source.Size.Width - 1);
                var x1 = Math.Min(x0 + 1, source.Size.Width - 1);
                var fx = Math.Clamp(sourceX - x0, 0d, 1d);
                var top = VisualLuma(source, x0, y0) + ((VisualLuma(source, x1, y0) - VisualLuma(source, x0, y0)) * fx);
                var bottom = VisualLuma(source, x0, y1) + ((VisualLuma(source, x1, y1) - VisualLuma(source, x0, y1)) * fx);
                result[(y * width) + x] = top + ((bottom - top) * fy);
            }
        }
        return result;
    }

    private static double VisualLuma(PixelImage source, int x, int y)
    {
        var (red, green, blue, alphaByte) = source.GetPixel(x, y);
        var alpha = alphaByte / 255d;
        var visualRed = (alpha * red) + ((1d - alpha) * 255d);
        var visualGreen = (alpha * green) + ((1d - alpha) * 255d);
        var visualBlue = (alpha * blue) + ((1d - alpha) * 255d);
        return (0.299d * visualRed) + (0.587d * visualGreen) + (0.114d * visualBlue);
    }
}
