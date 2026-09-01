using ImageLabPlugin.Domain.HybridImage;
using ImageLabPlugin.Domain.Imaging;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class HybridScaleAndDiagnosticsTests
{
    [Fact]
    public void ScaleProjector_AveragesRawBeforeByteQuantization()
    {
        var raw = new HybridLumaPlane(new ImageSize(2, 2), new[] { 0d, 1d / 255d, 0d, 1d / 255d });
        var preview = new HybridScaleProjector().CreateAll(raw).Single(item => item.Divisor == 2);
        Assert.Equal(0.5d / 255d, preview.Raw[0, 0], 15);
        Assert.Equal((byte)0, preview.Image.GetPixel(0, 0).R); // ToEven: 0.5 → 0
    }

    [Fact]
    public void ScaleProjector_OddDimensionsUseCeilingAndNeverReachZero()
    {
        var raw = new HybridLumaPlane(new ImageSize(3, 1), new[] { .1d, .2d, .3d });
        var previews = new HybridScaleProjector().CreateAll(raw);
        Assert.Equal(new ImageSize(2, 1), previews.Single(item => item.Divisor == 2).Raw.Size);
        Assert.Equal(new ImageSize(1, 1), previews.Single(item => item.Divisor == 8).Raw.Size);
    }

    [Fact]
    public void EdgeOverlay_MisalignedEdgesProduceSeparatedRedAndCyan()
    {
        var a = VerticalEdge(7, 5, 2);
        var b = VerticalEdge(7, 5, 4);
        var overlay = new HybridImageDiagnostics().CreateRedCyanEdgeOverlay(a, b);
        Assert.Contains(Enumerable.Range(0, 35), index =>
        {
            var pixel = overlay.GetPixel(index % 7, index / 7);
            return pixel.R > 0 && pixel.G == 0;
        });
        Assert.Contains(Enumerable.Range(0, 35), index =>
        {
            var pixel = overlay.GetPixel(index % 7, index / 7);
            return pixel.R == 0 && pixel.G > 0 && pixel.B > 0;
        });
    }

    [Fact]
    public void Cutoff_DecreasesAsSigmaIncreases()
    {
        Assert.True(GaussianPlaneFilter.FiftyPercentCutoff(2d) > GaussianPlaneFilter.FiftyPercentCutoff(8d));
    }

    private static HybridLumaPlane VerticalEdge(int width, int height, int edgeX)
    {
        var values = new double[width * height];
        for (var y = 0; y < height; y++)
            for (var x = edgeX; x < width; x++) values[(y * width) + x] = 1d;
        return new HybridLumaPlane(new ImageSize(width, height), values);
    }
}
