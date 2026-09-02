namespace ImageLabPlugin.Domain.Shared.Imaging;

/// <summary>只负责按给定最大边生成面积覆盖加权的缩小副本。</summary>
/// <remarks>
/// 这个类型刻意不知道各产品允许哪些尺寸档位：频域工具仍由自己的策略限制为 512/1024/2048，
/// SVD 则限制为 128/256。把“如何缩放”与“允许缩放到多大”分离后，两项产品能复用同一套抗混叠
/// 数值语义，又不会互相改变公开选项。小图不会放大，而是返回独立克隆，避免调用方共享可写像素。
/// </remarks>
internal sealed class ImageAreaResampler
{
    public PixelImage ResizeToMaximumEdge(
        PixelImage source,
        int maximumEdge,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (maximumEdge <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumEdge), maximumEdge, "最大边必须为正数。");
        }

        if (Math.Max(source.Size.Width, source.Size.Height) <= maximumEdge)
        {
            return source.Clone();
        }

        var scale = maximumEdge / (double)Math.Max(source.Size.Width, source.Size.Height);
        var width = Math.Max(1, (int)Math.Round(source.Size.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Size.Height * scale));
        return Resize(source, new ImageSize(width, height), cancellationToken);
    }

    /// <summary>按显式目标尺寸执行面积覆盖采样，供需要精确栅格尺寸的图案归一化复用。</summary>
    /// <remarks>
    /// 该重载只定义缩放数值，不决定 Contain、Stretch、背景颜色或二值阈值。调用方必须先完成自己的产品语义，
    /// 再把确定的 RGBA 图片和目标尺寸交给这里；这样面积抗混叠可以共享，而 Pattern 规则不会污染通用图像服务。
    /// </remarks>
    public PixelImage Resize(
        PixelImage source,
        ImageSize targetSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Size == targetSize) return source.Clone();
        var width = targetSize.Width;
        var height = targetSize.Height;
        var rgba = new byte[checked((int)(targetSize.PixelCount * 4))];
        var scaleX = source.Size.Width / (double)width;
        var scaleY = source.Size.Height / (double)height;
        var sums = new double[4];

        for (var targetY = 0; targetY < height; targetY++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var top = targetY * scaleY;
            var bottom = Math.Min(source.Size.Height, (targetY + 1) * scaleY);
            for (var targetX = 0; targetX < width; targetX++)
            {
                var left = targetX * scaleX;
                var right = Math.Min(source.Size.Width, (targetX + 1) * scaleX);
                Array.Clear(sums);
                double totalWeight = 0d;
                for (var sourceY = (int)Math.Floor(top); sourceY < Math.Ceiling(bottom); sourceY++)
                {
                    var yWeight = Math.Max(0d, Math.Min(bottom, sourceY + 1d) - Math.Max(top, sourceY));
                    for (var sourceX = (int)Math.Floor(left); sourceX < Math.Ceiling(right); sourceX++)
                    {
                        var xWeight = Math.Max(0d, Math.Min(right, sourceX + 1d) - Math.Max(left, sourceX));
                        var weight = xWeight * yWeight;
                        var pixel = source.GetPixel(
                            Math.Min(sourceX, source.Size.Width - 1),
                            Math.Min(sourceY, source.Size.Height - 1));
                        sums[0] += pixel.R * weight;
                        sums[1] += pixel.G * weight;
                        sums[2] += pixel.B * weight;
                        sums[3] += pixel.A * weight;
                        totalWeight += weight;
                    }
                }

                var offset = ((targetY * width) + targetX) * 4;
                for (var channel = 0; channel < 4; channel++)
                {
                    rgba[offset + channel] = (byte)Math.Clamp(
                        (int)Math.Round(sums[channel] / totalWeight), 0, 255);
                }
            }
        }

        return new PixelImage(targetSize, rgba);
    }
}
