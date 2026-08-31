using ImageLabPlugin.Domain.ColorTransfer;
using ImageLabPlugin.Domain.Comparison;
using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Tests;

internal static class ColorTransferTestFactory
{
    public static (SrgbColorSpace Srgb, CieLabColorSpace Lab, HsvColorSpace Hsv, CieDeltaE Delta,
        SrgbGamutMapper Gamut, ColorDistributionAnalyzer Distributions, RgbColorAggregator Aggregator,
        DominantColorClusterer Clusterer, PerceptualDifferenceAnalyzer Differences,
        LabStatisticsTransfer Transfer, FixedPaletteRemapper Remapper) Create()
    {
        var srgb = new SrgbColorSpace(); var lab = new CieLabColorSpace(); var hsv = new HsvColorSpace();
        var delta = new CieDeltaE(); var gamut = new SrgbGamutMapper(srgb, lab, delta);
        var distributions = new ColorDistributionAnalyzer(srgb, lab, hsv);
        var aggregator = new RgbColorAggregator(srgb, lab); var clusterer = new DominantColorClusterer(gamut, delta);
        var differences = new PerceptualDifferenceAnalyzer(srgb, lab, delta);
        var quality = new FullReferenceQualityAnalyzer(new ImagePairValidator());
        return (srgb, lab, hsv, delta, gamut, distributions, aggregator, clusterer, differences,
            new LabStatisticsTransfer(srgb, lab, gamut, distributions, differences, quality),
            new FixedPaletteRemapper(srgb, lab, distributions, differences, quality));
    }

    public static PixelImage Image(int width, int height, params byte[] rgba) => new(new ImageSize(width, height), rgba);
    public static CieLabColor ToLab(SrgbColor color)
    { var core = Create(); return core.Lab.ToLab(core.Srgb.ToXyz(core.Srgb.Decode(color))); }
}
