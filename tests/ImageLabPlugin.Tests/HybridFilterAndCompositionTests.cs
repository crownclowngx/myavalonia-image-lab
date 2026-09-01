using ImageLabPlugin.Domain.HybridImage;
using ImageLabPlugin.Domain.Imaging;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class HybridFilterAndCompositionTests
{
    [Fact]
    public void Kernel_IsOddSymmetricAndNormalized()
    {
        var kernel = new GaussianPlaneFilter().CreateKernel(2d);
        Assert.Equal(13, kernel.Length);
        Assert.Equal(1d, kernel.Sum(), 12);
        for (var i = 0; i < kernel.Length; i++) Assert.Equal(kernel[i], kernel[^(i + 1)], 15);
    }

    [Fact]
    public void Gaussian_ConstantPlane_RemainsConstant()
    {
        var source = new HybridLumaPlane(new ImageSize(7, 5), Enumerable.Repeat(.375d, 35).ToArray());
        var result = new GaussianPlaneFilter().Apply(source, 3d);
        Assert.All(result.Values.ToArray(), value => Assert.Equal(.375d, value, 12));
    }

    [Fact]
    public void Gaussian_ImpulseResponse_IsFiniteNormalizedAndCentered()
    {
        var values = new double[17 * 17];
        values[(8 * 17) + 8] = 1d;
        var result = new GaussianPlaneFilter().Apply(new HybridLumaPlane(new ImageSize(17, 17), values), 1.2d);
        Assert.Equal(1d, result.Values.Span.ToArray().Sum(), 12);
        Assert.Equal(result.Values.Span.ToArray().Max(), result[8, 8], 15);
        Assert.Equal(result[7, 8], result[9, 8], 15);
        Assert.All(result.Values.ToArray(), static value => Assert.True(double.IsFinite(value)));
    }

    [Fact]
    public void Gaussian_PreCanceled_RejectsBeforeReturningPartialPlane()
    {
        var plane = new HybridLumaPlane(new ImageSize(8, 8), Enumerable.Repeat(.5d, 64).ToArray());
        Assert.Throws<OperationCanceledException>(() => new GaussianPlaneFilter().Apply(
            plane, 1d, new CancellationToken(canceled: true)));
    }

    [Fact]
    public void Reflect101_MapsNegativeAndPositiveBoundaries()
    {
        Assert.Equal(1, GaussianPlaneFilter.Reflect101(-1, 4));
        Assert.Equal(2, GaussianPlaneFilter.Reflect101(4, 4));
        Assert.Equal(0, GaussianPlaneFilter.Reflect101(0, 1));
    }

    [Fact]
    public void Compose_ConstantB_HighPassIsZero()
    {
        var size = new ImageSize(5, 5);
        var a = new HybridLumaPlane(size, Enumerable.Repeat(.25d, 25).ToArray());
        var b = new HybridLumaPlane(size, Enumerable.Repeat(.75d, 25).ToArray());
        var result = new HybridImageComposer(new GaussianPlaneFilter()).Compose(a, b, 1d, 1d, 1d, 1d);
        Assert.All(result.HighB.Values.ToArray(), value => Assert.InRange(Math.Abs(value), 0d, 1e-12));
        Assert.All(result.Raw.Values.ToArray(), value => Assert.Equal(.25d, value, 12));
    }

    [Fact]
    public void Compose_SameImageAndSameSigma_ReconstructsOriginalRaw()
    {
        var random = new Random(20260901);
        var values = Enumerable.Range(0, 81).Select(_ => random.NextDouble()).ToArray();
        var source = new HybridLumaPlane(new ImageSize(9, 9), values);
        var result = new HybridImageComposer(new GaussianPlaneFilter()).Compose(source, source, 1.4d, 1.4d, 1d, 1d);
        for (var i = 0; i < values.Length; i++) Assert.Equal(values[i], result.Raw.Values.Span[i], 12);
    }

    [Fact]
    public void Gaussian_RandomConstantPlanes_PreserveProperty()
    {
        var random = new Random(41);
        var filter = new GaussianPlaneFilter();
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var constant = random.NextDouble();
            var source = new HybridLumaPlane(new ImageSize(6, 5), Enumerable.Repeat(constant, 30).ToArray());
            var result = filter.Apply(source, .8d + (random.NextDouble() * 3d));
            Assert.All(result.Values.ToArray(), value => Assert.Equal(constant, value, 11));
        }
    }

    [Fact]
    public void Compose_PreservesSignedHighAndReportsClipping()
    {
        var size = new ImageSize(3, 3);
        var a = new HybridLumaPlane(size, Enumerable.Repeat(0d, 9).ToArray());
        var b = new HybridLumaPlane(size, new[] { 0d, 0d, 0d, 0d, 1d, 0d, 0d, 0d, 0d });
        var result = new HybridImageComposer(new GaussianPlaneFilter()).Compose(a, b, .8d, .8d, 0d, 2d);
        Assert.Contains(result.HighB.Values.ToArray(), static value => value < 0d);
        Assert.Contains(result.HighB.Values.ToArray(), static value => value > 0d);
        Assert.True(result.Statistics.ClippedPixelCount > 0);
    }

    [Fact]
    public void Compose_BothGainsZero_ReturnsOpaqueBlack()
    {
        var size = new ImageSize(2, 2);
        var plane = new HybridLumaPlane(size, new[] { .1, .2, .3, .4 });
        var result = new HybridImageComposer(new GaussianPlaneFilter()).Compose(plane, plane, 32d, 32d, 0d, 0d);
        Assert.All(result.Quantized.Rgba.ToArray().Chunk(4), pixel => Assert.Equal(new byte[] { 0, 0, 0, 255 }, pixel));
    }

    [Fact]
    public void LumaProjector_CompositesTransparencyOnWhiteBeforeLuma()
    {
        var image = new PixelImage(new ImageSize(1, 1), new byte[] { 0, 0, 0, 0 });
        Assert.Equal(1d, new HybridLumaProjector().Project(image)[0, 0], 12);
    }
}
