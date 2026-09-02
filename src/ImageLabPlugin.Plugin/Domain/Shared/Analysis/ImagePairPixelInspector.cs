using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Domain.Shared.Analysis;

/// <summary>读取同一原图坐标的像素对，不从显示代理推测原始像素。</summary>
internal sealed class ImagePairPixelInspector(ImagePairValidator validator)
{
    public ImagePairPixelReport Inspect(PixelImage reference, PixelImage candidate, ImagePoint point)
    {
        validator.EnsureComparable(reference, candidate);
        var first = reference.GetPixel(point.X, point.Y);
        var second = candidate.GetPixel(point.X, point.Y);
        var firstLuma = ColorSpaceConverter.ToLuma(first.R, first.G, first.B);
        var secondLuma = ColorSpaceConverter.ToLuma(second.R, second.G, second.B);
        return new ImagePairPixelReport(
            point,
            new RgbaPixel(first.R, first.G, first.B, first.A),
            new RgbaPixel(second.R, second.G, second.B, second.A),
            firstLuma,
            secondLuma,
            second.R - first.R,
            second.G - first.G,
            second.B - first.B,
            second.A - first.A,
            secondLuma - firstLuma);
    }
}
