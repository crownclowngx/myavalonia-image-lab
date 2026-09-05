using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Domain.SeamCarving;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class SeamPlanningAndReferenceTests
{
    [Fact]
    public void Auto按绝对变化比例排序且相等时宽优先()
    {
        var planner = new SeamResizePlanner(new SeamResourceEstimator());
        var size = new ImageSize(100, 100);
        var equal = planner.Plan("image", "mask", size,
            new(new ImageSize(90, 90), SeamAxisOrder.Auto, ReferenceResizeAlgorithm.Bilinear));
        Assert.Equal(SeamOrientation.Vertical, equal.Steps[0].Orientation);
        var heightLarger = planner.Plan("image", "mask", size,
            new(new ImageSize(95, 80), SeamAxisOrder.Auto, ReferenceResizeAlgorithm.Bilinear));
        Assert.Equal(SeamOrientation.Horizontal, heightLarger.Steps[0].Orientation);
    }

    [Fact]
    public void 显式轴顺序覆盖Auto且步骤操作准确()
    {
        var planner = new SeamResizePlanner(new SeamResourceEstimator());
        var plan = planner.Plan("image", "mask", new ImageSize(100, 100),
            new(new ImageSize(105, 95), SeamAxisOrder.HeightFirst, ReferenceResizeAlgorithm.BicubicCatmullRom));
        Assert.All(plan.Steps.Take(5), item => { Assert.Equal(SeamOrientation.Horizontal, item.Orientation); Assert.Equal(SeamOperation.Remove, item.Operation); });
        Assert.All(plan.Steps.Skip(5), item => { Assert.Equal(SeamOrientation.Vertical, item.Orientation); Assert.Equal(SeamOperation.Insert, item.Operation); });
    }

    [Fact]
    public void 资源预算在所有冻结边界内允许()
    {
        var estimate = new SeamResourceEstimator().Estimate(new ImageSize(1000, 1000), new ImageSize(1000, 1000));
        Assert.True(estimate.IsAllowed);
        Assert.Equal(0, estimate.TotalSeams);
        Assert.Equal(1_000_000, estimate.MaximumWorkingPixels);
        Assert.True(estimate.EstimatedPeakBytes > 0);
    }

    [Theory]
    [InlineData(4, 3, 3, 3, 12)]
    [InlineData(4, 3, 5, 3, 24)]
    public void 单元访问等差公式对删除与含影子插入给出精确Golden(
        int width, int height, int targetWidth, int targetHeight, long expectedVisits)
    {
        var estimate = new SeamResourceEstimator().Estimate(
            new ImageSize(width, height), new ImageSize(targetWidth, targetHeight));
        Assert.Equal(expectedVisits, estimate.EstimatedCellVisits);
    }

    [Fact]
    public void 单像素轴放大在规划前阻断并给出邻居建议()
    {
        var estimate = new SeamResourceEstimator().Estimate(new ImageSize(1, 4), new ImageSize(2, 4));
        Assert.False(estimate.IsAllowed);
        Assert.Contains(estimate.BlockingReasons, item => item.Contains("插值邻居", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(1000, 1000, 1257, 1000, "总缝数")]
    [InlineData(1000, 1000, 1251, 1000, "宽度变化")]
    [InlineData(2000, 1000, 2000, 1001, "工作图像素")]
    public void 资源预算超界给出实际值上限与建议(int width, int height, int targetWidth, int targetHeight, string expected)
    {
        var estimate = new SeamResourceEstimator().Estimate(new ImageSize(width, height), new ImageSize(targetWidth, targetHeight));
        Assert.False(estimate.IsAllowed);
        Assert.Contains(estimate.BlockingReasons, item => item.Contains(expected, StringComparison.Ordinal));
        Assert.All(estimate.BlockingReasons, item => Assert.Contains("上限", item, StringComparison.Ordinal));
    }

    [Fact]
    public void 蒙版笔划后画覆盖且擦除恢复普通()
    {
        var strokes = new[]
        {
            new SeamBrushStroke(SeamBrushTool.Protect, 0.1, [new(0.5, 0.5)], 0),
            new SeamBrushStroke(SeamBrushTool.PreferRemoval, 0.1, [new(0.5, 0.5)], 1),
            new SeamBrushStroke(SeamBrushTool.Erase, 0.05, [new(0.5, 0.5)], 2)
        };
        var mask = new SeamMaskRasterizer().Rasterize(new ImageSize(20, 20), strokes);
        Assert.Equal(SeamMaskValue.Normal, mask.Get(10, 10));
        Assert.Contains(mask.Values.ToArray(), value => value == (byte)SeamMaskValue.PreferRemoval);
    }

    [Fact]
    public void 区域纹理投影不修改工作图且两类区域可辨()
    {
        var image = SeamEnergyTests.Solid(8, 1, 40, 40, 40, 0);
        var mask = new SeamMask(image.Size); mask.Set(0, 0, SeamMaskValue.Protect); mask.Set(7, 0, SeamMaskValue.PreferRemoval);
        var before = image.Rgba.ToArray();
        var preview = new SeamMaskPreviewProjector().Project(image, mask);
        Assert.Equal(before, image.Rgba.ToArray());
        Assert.NotEqual(preview.GetPixel(0, 0), preview.GetPixel(7, 0));
        Assert.True(preview.GetPixel(0, 0).A >= 180); Assert.True(preview.GetPixel(7, 0).A >= 180);
    }

    [Fact]
    public void 笔划数量和归一化输入有界()
    {
        var tooMany = Enumerable.Range(0, 513).Select(index =>
            new SeamBrushStroke(SeamBrushTool.Protect, 0.1, [new(0.5, 0.5)], index)).ToArray();
        Assert.Throws<ArgumentOutOfRangeException>(() => new SeamMaskRasterizer().Rasterize(new ImageSize(2, 2), tooMany));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SeamBrushStroke(SeamBrushTool.Protect, 0.1, [new(2, 0)], 0).Validate());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void 两个参考Strategy保持常量与Alpha并精确命中目标尺寸(int algorithmValue)
    {
        var algorithm = (ReferenceResizeAlgorithm)algorithmValue;
        IReferenceImageResampler resampler = algorithm == ReferenceResizeAlgorithm.Bilinear
            ? new BilinearReferenceResampler() : new BicubicReferenceResampler();
        var source = SeamEnergyTests.Solid(3, 2, 12, 34, 56, 78);
        var result = resampler.Resize(source, new ImageSize(7, 5));
        Assert.Equal(new ImageSize(7, 5), result.Size);
        for (var y = 0; y < result.Size.Height; y++)
            for (var x = 0; x < result.Size.Width; x++) Assert.Equal(((byte)12, (byte)34, (byte)56, (byte)78), result.GetPixel(x, y));
    }

    [Fact]
    public void 双线性像素中心映射的中心Golden()
    {
        var source = SeamEnergyTests.Image(2, 1, 0, 0, 0, 255, 200, 100, 50, 255);
        var result = new BilinearReferenceResampler().Resize(source, new ImageSize(3, 1));
        Assert.Equal(((byte)100, (byte)50, (byte)25, (byte)255), result.GetPixel(1, 0));
    }

    [Fact]
    public void 双三次核权重在任意分数位置和为一()
    {
        const double position = 0.37;
        var sum = Enumerable.Range(-1, 4).Sum(offset => BicubicReferenceResampler.Kernel(position - offset));
        Assert.Equal(1d, sum, 12);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void 参考缩放已取消时不产生迟到结果(int algorithmValue)
    {
        var algorithm = (ReferenceResizeAlgorithm)algorithmValue;
        IReferenceImageResampler resampler = algorithm == ReferenceResizeAlgorithm.Bilinear
            ? new BilinearReferenceResampler() : new BicubicReferenceResampler();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => resampler.Resize(
            SeamEnergyTests.Solid(2, 2, 1, 2, 3, 4), new ImageSize(3, 3), cancellation.Token));
    }
}
