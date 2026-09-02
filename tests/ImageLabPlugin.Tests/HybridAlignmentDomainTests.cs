using System.Globalization;
using ImageLabPlugin.Domain.HybridImage;
using ImageLabPlugin.Domain.Shared.Imaging;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class HybridAlignmentDomainTests
{
    [Fact]
    public void Solve_TwoPoints_RecoversTransformAndMarksUnvalidated()
    {
        var pairs = new[]
        {
            Pair(1, .2, .2, .1, .2),
            Pair(2, .8, .2, .4, .2)
        };
        var result = new SimilarityTransformSolver().Solve(pairs, new ImageSize(101, 101), new ImageSize(101, 101));

        Assert.Equal(2d, result.Transform.Scale, 10);
        Assert.Equal(HybridResidualStatus.NotIndependentlyValidated, result.ResidualStatus);
        Assert.InRange(result.RmsResidualPixels, 0d, 1e-10);
    }

    [Fact]
    public void Solve_ThreePoints_RecoversRotationAndTranslation()
    {
        var pairs = new[]
        {
            Pair(1, .3, .2, .2, .3), Pair(2, .3, .8, .8, .3), Pair(3, .1, .5, .5, .5)
        };
        var result = new SimilarityTransformSolver().Solve(pairs, new ImageSize(101, 101), new ImageSize(101, 101));

        Assert.Equal(90d, result.Transform.RotationDegrees, 10);
        Assert.Equal(HybridResidualStatus.Measured, result.ResidualStatus);
        Assert.InRange(result.MaximumResidualPixels, 0d, 1e-9);
    }

    [Fact]
    public void Solve_MirroredPoints_AreRejected()
    {
        var pairs = new[]
        {
            Pair(1, .2, .2, .8, .2), Pair(2, .8, .2, .2, .2), Pair(3, .2, .8, .8, .8)
        };
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new SimilarityTransformSolver().Solve(pairs, new ImageSize(101, 101), new ImageSize(101, 101)));
        Assert.Contains("镜像", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Solve_ShortBaseline_IsRejectedBeforeUnstableMath()
    {
        var pairs = new[] { Pair(1, .5, .5, .5, .5), Pair(2, .5001, .5, .5001, .5) };
        Assert.Throws<InvalidOperationException>(() =>
            new SimilarityTransformSolver().Solve(pairs, new ImageSize(100, 100), new ImageSize(100, 100)));
    }

    [Fact]
    public void Transform_AnalyticInverse_RoundTrips()
    {
        var transform = new HybridSimilarityTransform(1.7d, -.43d, 12.5d, -8.25d);
        var point = new HybridPoint(19.25d, 31.75d);
        var roundTrip = transform.MapAToB(transform.MapBToA(point));
        Assert.Equal(point.X, roundTrip.X, 11);
        Assert.Equal(point.Y, roundTrip.Y, 11);
    }

    [Fact]
    public void RecipeFingerprint_IsCultureAndInputOrderIndependent()
    {
        var oldCulture = CultureInfo.CurrentCulture;
        try
        {
            var points = new[] { Pair(2, .8, .8, .7, .7), Pair(1, .2, .2, .1, .1) };
            var recipe = new HybridImageRecipe(points, new HybridNormalizedCrop(.1, .2, .8, .9));
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var first = recipe.Fingerprint();
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("zh-CN");
            var second = new HybridImageRecipe(points.Reverse().ToArray(), recipe.Crop).Fingerprint();
            Assert.Equal(first, second);
        }
        finally { CultureInfo.CurrentCulture = oldCulture; }
    }

    [Fact]
    public void NormalizedCrop_RoundTrip_PreservesIntegerRectangle()
    {
        var size = new ImageSize(100, 80);
        var crop = new HybridCropRectangle(10, 16, 60, 48);
        Assert.Equal(crop, HybridNormalizedCrop.FromPixels(crop, size).ToPixels(size));
    }

    private static HybridAlignmentPointPair Pair(int id, double ax, double ay, double bx, double by) =>
        new(id, new HybridNormalizedPoint(ax, ay), new HybridNormalizedPoint(bx, by));
}
