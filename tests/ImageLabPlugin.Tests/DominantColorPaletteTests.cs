using ImageLabPlugin.Application.ColorTransfer;
using ImageLabPlugin.Domain.ColorTransfer;
using ImageLabPlugin.Domain.Shared.Imaging;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class DominantColorPaletteTests
{
    [Fact]
    public void 二色图聚类确定且权重守恒()
    {
        var image = ColorTransferTestFactory.Image(4, 1,
            255, 0, 0, 255, 255, 0, 0, 255, 0, 0, 255, 255, 0, 0, 255, 255);
        var core = ColorTransferTestFactory.Create(); var cells = core.Aggregator.Aggregate(image, default);
        var first = core.Clusterer.Cluster(cells, 2, PaletteSource.Target, default);
        var second = core.Clusterer.Cluster(cells, 2, PaletteSource.Target, default);
        Assert.True(first.Converged); Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(4d, first.Entries.Sum(entry => entry.Weight), 12);
        Assert.Equal(new[] { 0, 1 }, first.Entries.Select(entry => entry.ClusterIndex));
    }

    [Fact]
    public void 同一五Bit单元使用实际Alpha加权均值而不是几何中心()
    {
        var image = ColorTransferTestFactory.Image(2, 1, 8, 8, 8, 255, 15, 15, 15, 128);
        var cells = ColorTransferTestFactory.Create().Aggregator.Aggregate(image, default);
        Assert.Single(cells); Assert.Equal(1d + (128d / 255d), cells[0].Weight, 12);
        var expected = ((8d / 255d) + ((128d / 255d) * (15d / 255d))) / (1d + (128d / 255d));
        Assert.Equal(expected, cells[0].Srgb.Red, 12); Assert.Equal(cells[0].Srgb.Red, cells[0].Srgb.Green, 12);
    }

    [Fact]
    public void 显示排序不改变调色板身份()
    {
        var entries = new[]
        {
            new PaletteEntry(0, new SrgbColor(1,0,0), ColorTransferTestFactory.ToLab(new SrgbColor(1,0,0)), 1, .25, 0, 0),
            new PaletteEntry(1, new SrgbColor(0,0,1), ColorTransferTestFactory.ToLab(new SrgbColor(0,0,1)), 3, .75, 0, 0)
        };
        var sorter = new PaletteSorter(new HsvColorSpace());
        Assert.Equal(new[] { 1, 0 }, sorter.Sort(entries, PaletteSort.Proportion).Select(item => item.ClusterIndex));
        Assert.Equal(DominantColorClusterer.Fingerprint(entries), DominantColorClusterer.Fingerprint(sorter.Sort(entries, PaletteSort.Hue)));
    }

    [Fact]
    public void 重映射只输出冻结颜色并逐字节保持Alpha透明像素()
    {
        var target = ColorTransferTestFactory.Image(3, 1,
            240, 10, 10, 64, 10, 10, 240, 255, 77, 88, 99, 0);
        var red = new SrgbColor(1, 0, 0); var blue = new SrgbColor(0, 0, 1);
        var entries = new[]
        {
            new PaletteEntry(0, red, ColorTransferTestFactory.ToLab(red), 1, .5, 0, 0),
            new PaletteEntry(1, blue, ColorTransferTestFactory.ToLab(blue), 1, .5, 0, 0)
        };
        var palette = new FrozenPalette("source", entries, PaletteSource.Target, "frozen");
        var result = ColorTransferTestFactory.Create().Remapper.Remap(target, palette, default);
        Assert.Equal((byte)64, result.Image.GetAlpha(0, 0)); Assert.Equal((byte)255, result.Image.GetAlpha(1, 0));
        Assert.Equal((77, 88, 99, 0), result.Image.GetPixel(2, 0));
        Assert.Contains(result.Image.GetPixel(0, 0).R, new byte[] { 0, 255 }); Assert.Equal(2, result.PalettePixelCounts.Sum());
    }

    [Fact]
    public void Session换图使结果调色板与导出资格失效()
    {
        using var session = new ColorTransferSession();
        var image = ColorTransferTestFactory.Image(1, 1, 1, 2, 3, 255);
        session.SetTarget(new PreparedColorImage("one.png", image, image.Clone(), "one"));
        session.SetFrozenPalette(new FrozenPalette("x", Array.Empty<PaletteEntry>(), PaletteSource.Target, "x"));
        session.SetTarget(new PreparedColorImage("two.png", image, image.Clone(), "two"));
        Assert.Null(session.FrozenPalette); Assert.False(session.HasCurrentResult);
    }

    [Fact]
    public void 像素探针分别使用图片坐标并为透明像素返回PaletteN()
    {
        var core = ColorTransferTestFactory.Create();
        var inspector = new ColorPixelInspector(core.Srgb, core.Lab, core.Hsv, core.Delta);
        var image = ColorTransferTestFactory.Image(2, 1, 255, 0, 0, 255, 7, 8, 9, 0);
        var red = new SrgbColor(1, 0, 0);
        var palette = new FrozenPalette("s", [new PaletteEntry(3, red, ColorTransferTestFactory.ToLab(red), 1, 1, 0, 0)], PaletteSource.Target, "f");
        Assert.Equal(3, inspector.Inspect(image, 0, 0, palette).PaletteClusterIndex);
        Assert.Null(inspector.Inspect(image, 1, 0, palette).PaletteClusterIndex);
        Assert.Throws<ArgumentOutOfRangeException>(() => inspector.Inspect(image, 2, 0, palette));
    }
}
