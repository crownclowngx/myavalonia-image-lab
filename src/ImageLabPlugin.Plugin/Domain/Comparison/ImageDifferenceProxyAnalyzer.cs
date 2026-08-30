using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Domain.Comparison;

/// <summary>保存同一有界网格上的 RGB、MaxRGB 与 Y 基础差异。</summary>
/// <remarks>数组值是未放大的 0..255 差异；改变显示倍率时只需重新着色，不必重新扫描完整图片。</remarks>
internal sealed class ImageDifferenceProxy
{
    public ImageDifferenceProxy(ImageSize size, byte[] red, byte[] green, byte[] blue, byte[] maximumRgb, byte[] luma)
    {
        var expected = checked((int)size.PixelCount);
        if (red.Length != expected || green.Length != expected || blue.Length != expected ||
            maximumRgb.Length != expected || luma.Length != expected)
            throw new ArgumentException("基础差异场的通道长度与尺寸不一致。", nameof(red));
        Size = size;
        Red = red; Green = green; Blue = blue; MaximumRgb = maximumRgb; Luma = luma;
    }

    public ImageSize Size { get; }
    public ReadOnlyMemory<byte> Red { get; }
    public ReadOnlyMemory<byte> Green { get; }
    public ReadOnlyMemory<byte> Blue { get; }
    public ReadOnlyMemory<byte> MaximumRgb { get; }
    public ReadOnlyMemory<byte> Luma { get; }
}

/// <summary>先计算原分辨率绝对差异，再按目标格面积平均，防止相反变化在缩图前互相抵消。</summary>
internal sealed class ImageDifferenceProxyAnalyzer(ImagePairValidator validator)
{
    public ImageDifferenceProxy Analyze(
        PixelImage reference,
        PixelImage candidate,
        int maximumEdge = 1024,
        CancellationToken cancellationToken = default)
    {
        validator.EnsureComparable(reference, candidate);
        if (maximumEdge <= 0) throw new ArgumentOutOfRangeException(nameof(maximumEdge));
        var target = CalculateTarget(reference.Size, maximumEdge);
        var count = checked((int)target.PixelCount);
        var red = new byte[count]; var green = new byte[count]; var blue = new byte[count];
        var maximum = new byte[count]; var luma = new byte[count];
        var scaleX = reference.Size.Width / (double)target.Width;
        var scaleY = reference.Size.Height / (double)target.Height;
        var first = reference.Rgba.Span; var second = candidate.Rgba.Span;

        for (var targetY = 0; targetY < target.Height; targetY++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var top = targetY * scaleY; var bottom = Math.Min(reference.Size.Height, (targetY + 1) * scaleY);
            for (var targetX = 0; targetX < target.Width; targetX++)
            {
                var left = targetX * scaleX; var right = Math.Min(reference.Size.Width, (targetX + 1) * scaleX);
                double sumR = 0d, sumG = 0d, sumB = 0d, sumMax = 0d, sumLuma = 0d, total = 0d;
                for (var sourceY = (int)Math.Floor(top); sourceY < Math.Ceiling(bottom); sourceY++)
                {
                    var yWeight = Math.Max(0d, Math.Min(bottom, sourceY + 1d) - Math.Max(top, sourceY));
                    for (var sourceX = (int)Math.Floor(left); sourceX < Math.Ceiling(right); sourceX++)
                    {
                        var xWeight = Math.Max(0d, Math.Min(right, sourceX + 1d) - Math.Max(left, sourceX));
                        var weight = xWeight * yWeight;
                        var offset = ((sourceY * reference.Size.Width) + sourceX) * 4;
                        var dr = Math.Abs(second[offset] - first[offset]);
                        var dg = Math.Abs(second[offset + 1] - first[offset + 1]);
                        var db = Math.Abs(second[offset + 2] - first[offset + 2]);
                        var firstY = ColorSpaceConverter.ToLuma(first[offset], first[offset + 1], first[offset + 2]);
                        var secondY = ColorSpaceConverter.ToLuma(second[offset], second[offset + 1], second[offset + 2]);
                        sumR += dr * weight; sumG += dg * weight; sumB += db * weight;
                        sumMax += Math.Max(dr, Math.Max(dg, db)) * weight;
                        sumLuma += Math.Abs(secondY - firstY) * weight; total += weight;
                    }
                }

                var index = (targetY * target.Width) + targetX;
                red[index] = Round(sumR / total); green[index] = Round(sumG / total); blue[index] = Round(sumB / total);
                maximum[index] = Round(sumMax / total); luma[index] = Round(sumLuma / total);
            }
        }

        return new ImageDifferenceProxy(target, red, green, blue, maximum, luma);
    }

    private static ImageSize CalculateTarget(ImageSize source, int maximumEdge)
    {
        if (Math.Max(source.Width, source.Height) <= maximumEdge) return source;
        var scale = maximumEdge / (double)Math.Max(source.Width, source.Height);
        return new ImageSize(Math.Max(1, (int)Math.Round(source.Width * scale)), Math.Max(1, (int)Math.Round(source.Height * scale)));
    }

    private static byte Round(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);
}
