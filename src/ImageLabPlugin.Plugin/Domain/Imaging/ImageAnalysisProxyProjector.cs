namespace ImageLabPlugin.Domain.Imaging;

/// <summary>生成受控最大边的抗混叠分析代理。</summary>
/// <remarks>
/// 缩小时每个目标像素按其覆盖的源像素面积加权平均。该实现比最近邻慢一些，但不会把高频纹理折叠成
/// 虚假低频峰；小图不放大并直接克隆，使全通重建可以保持逐字节一致。
/// </remarks>
internal sealed class ImageAnalysisProxyProjector
{
    public static readonly int[] SupportedMaximumEdges = [512, 1024, 2048];

    public PixelImage Create(PixelImage source, int maximumEdge, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!SupportedMaximumEdges.Contains(maximumEdge))
        {
            throw new ArgumentOutOfRangeException(nameof(maximumEdge), maximumEdge, "分析档位只能是 512、1024 或 2048。 ");
        }

        if (Math.Max(source.Size.Width, source.Size.Height) <= maximumEdge)
        {
            return source.Clone();
        }

        var scale = maximumEdge / (double)Math.Max(source.Size.Width, source.Size.Height);
        var width = Math.Max(1, (int)Math.Round(source.Size.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Size.Height * scale));
        var targetSize = new ImageSize(width, height);
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
                        var pixel = source.GetPixel(Math.Min(sourceX, source.Size.Width - 1), Math.Min(sourceY, source.Size.Height - 1));
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
                    rgba[offset + channel] = (byte)Math.Clamp((int)Math.Round(sums[channel] / totalWeight), 0, 255);
                }
            }
        }

        return new PixelImage(targetSize, rgba);
    }
}
