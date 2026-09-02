using ImageLabPlugin.Domain.HybridImage;
using ImageLabPlugin.Domain.Shared.Imaging;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class HybridWarpAndCropTests
{
    [Fact]
    public void Warp_Identity_UsesPixelCentersAndMarksLastRowAndColumnInvalid()
    {
        var source = Plane(3, 3, 0, 1, 2, 3, 4, 5, 6, 7, 8);
        var result = new AlignedImageSampler().Warp(source, source.Size,
            new HybridSimilarityTransform(1d, 0d, 0d, 0d));

        Assert.Equal(0d, result.AlignedB[0, 0]);
        Assert.Equal(4d, result.AlignedB[1, 1]);
        Assert.True(result.IsValid(1, 1));
        Assert.False(result.IsValid(2, 1));
        Assert.False(result.IsValid(1, 2));
    }

    [Fact]
    public void Warp_SubpixelTranslation_PerformsBilinearGolden()
    {
        var source = Plane(2, 2, 0, 1, 2, 3);
        var result = new AlignedImageSampler().Warp(source, new ImageSize(1, 1),
            new HybridSimilarityTransform(1d, 0d, -.5d, -.5d));
        Assert.True(result.IsValid(0, 0));
        Assert.Equal(1.5d, result.AlignedB[0, 0], 12);
    }

    [Fact]
    public void Warp_PreCanceled_DoesNotReturnPartialResult()
    {
        var cancellation = new CancellationToken(canceled: true);
        Assert.Throws<OperationCanceledException>(() => new AlignedImageSampler().Warp(
            Plane(2, 2, 0, 1, 2, 3), new ImageSize(2, 2),
            new HybridSimilarityTransform(1d, 0d, 0d, 0d), cancellation));
    }

    [Fact]
    public void MaximumRectangle_UsesStableTieBreak()
    {
        var mask = new[]
        {
            true, true, false, true, true,
            true, true, false, true, true
        };
        var crop = new HybridCropValidator().FindMaximumValidRectangle(new ImageSize(5, 2), mask);
        Assert.Equal(new HybridCropRectangle(0, 0, 2, 2), crop);
    }

    [Fact]
    public void MaximumRectangle_MatchesSmallBruteForceCases()
    {
        var random = new Random(17);
        var validator = new HybridCropValidator();
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var mask = Enumerable.Range(0, 20).Select(_ => random.Next(2) == 1).ToArray();
            if (!mask.Any(static value => value)) { attempt--; continue; }
            var actual = validator.FindMaximumValidRectangle(new ImageSize(5, 4), mask);
            var maximumArea = BruteForceMaximumArea(mask, 5, 4);
            Assert.Equal(maximumArea, actual.Area);
        }
    }

    [Fact]
    public void UserCrop_CannotExpandOutsideValidRectangle()
    {
        var validator = new HybridCropValidator();
        validator.ValidateUserCrop(new HybridCropRectangle(2, 2, 4, 4), new HybridCropRectangle(1, 1, 8, 8));
        Assert.Throws<ArgumentException>(() => validator.ValidateUserCrop(
            new HybridCropRectangle(0, 2, 4, 4), new HybridCropRectangle(1, 1, 8, 8)));
    }

    private static long BruteForceMaximumArea(bool[] mask, int width, int height)
    {
        long best = 0;
        for (var top = 0; top < height; top++)
            for (var left = 0; left < width; left++)
                for (var bottom = top + 1; bottom <= height; bottom++)
                    for (var right = left + 1; right <= width; right++)
                    {
                        var valid = true;
                        for (var y = top; y < bottom && valid; y++)
                            for (var x = left; x < right; x++) valid &= mask[(y * width) + x];
                        if (valid) best = Math.Max(best, (long)(right - left) * (bottom - top));
                    }
        return best;
    }

    private static HybridLumaPlane Plane(int width, int height, params double[] values) =>
        new(new ImageSize(width, height), values);
}
