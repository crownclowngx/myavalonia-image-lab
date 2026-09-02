using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Domain.SeamCarving;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class SeamRemovalAndInsertionTests
{
    [Fact]
    public void 垂直删除按每行坐标逐字节搬移且同步蒙版()
    {
        var image = CoordinateImage(3, 2);
        var mask = new SeamMask(image.Size); mask.Set(1, 0, SeamMaskValue.Protect); mask.Set(0, 1, SeamMaskValue.PreferRemoval);
        var path = new SeamPath(SeamOrientation.Vertical, image.Size, [1, 0], 0, 0, 1, 1);
        var result = new SeamRemover().Remove(image, mask, path);
        Assert.Equal(new ImageSize(2, 2), result.Image.Size);
        Assert.Equal(image.GetPixel(0, 0), result.Image.GetPixel(0, 0));
        Assert.Equal(image.GetPixel(2, 0), result.Image.GetPixel(1, 0));
        Assert.Equal(image.GetPixel(1, 1), result.Image.GetPixel(0, 1));
        Assert.Equal(SeamMaskValue.Normal, result.Mask.Get(0, 0));
        Assert.Equal(SeamMaskValue.Normal, result.Mask.Get(0, 1));
    }

    [Fact]
    public void 水平删除按每列坐标逐字节搬移()
    {
        var image = CoordinateImage(2, 3);
        var path = new SeamPath(SeamOrientation.Horizontal, image.Size, [1, 2], 0, 0, 0, 0);
        var result = new SeamRemover().Remove(image, new SeamMask(image.Size), path);
        Assert.Equal(new ImageSize(2, 2), result.Image.Size);
        Assert.Equal(image.GetPixel(0, 2), result.Image.GetPixel(0, 1));
        Assert.Equal(image.GetPixel(1, 1), result.Image.GetPixel(1, 1));
    }

    [Fact]
    public void 删除拒绝宽高为一或过期尺寸路径()
    {
        var narrow = CoordinateImage(1, 2);
        var vertical = new SeamPath(SeamOrientation.Vertical, narrow.Size, [0, 0], 0, 0, 0, 0);
        Assert.Throws<InvalidOperationException>(() => new SeamRemover().Remove(narrow, new SeamMask(narrow.Size), vertical));
        var other = CoordinateImage(2, 2);
        Assert.Throws<InvalidOperationException>(() => new SeamRemover().Remove(other, new SeamMask(other.Size), vertical));
    }

    [Fact]
    public void 垂直插入位于缝右侧并使用预乘Alpha插值()
    {
        var image = SeamEnergyTests.Image(2, 1, 255, 0, 0, 255, 0, 255, 0, 0);
        var path = new SeamInsertionPath(SeamOrientation.Vertical, image.Size, [0]);
        var result = new SeamInserter().Insert(image, new SeamMask(image.Size), path, []);
        Assert.Equal(new ImageSize(3, 1), result.Image.Size);
        Assert.Equal((byte)255, result.Image.GetPixel(1, 0).R);
        Assert.Equal((byte)0, result.Image.GetPixel(1, 0).G);
        Assert.Equal((byte)128, result.Image.GetPixel(1, 0).A);
        Assert.Equal(image.GetPixel(1, 0), result.Image.GetPixel(2, 0));
    }

    [Fact]
    public void 两个全透明邻居插值固定清空隐藏Rgb()
    {
        var image = SeamEnergyTests.Image(2, 1, 255, 20, 30, 0, 1, 2, 3, 0);
        var path = new SeamInsertionPath(SeamOrientation.Vertical, image.Size, [0]);
        var inserted = new SeamInserter().Insert(image, new SeamMask(image.Size), path, []).Image;
        Assert.Equal(((byte)0, (byte)0, (byte)0, (byte)0), inserted.GetPixel(1, 0));
    }

    [Fact]
    public void 插入蒙版按保护优先再删除传播()
    {
        var image = CoordinateImage(3, 1);
        var mask = new SeamMask(image.Size);
        mask.Set(0, 0, SeamMaskValue.PreferRemoval); mask.Set(1, 0, SeamMaskValue.Protect);
        var first = new SeamInserter().Insert(image, mask,
            new SeamInsertionPath(SeamOrientation.Vertical, image.Size, [0]), []);
        Assert.Equal(SeamMaskValue.Protect, first.Mask.Get(1, 0));
        var removalOnly = new SeamMask(image.Size); removalOnly.Set(0, 0, SeamMaskValue.PreferRemoval);
        var second = new SeamInserter().Insert(image, removalOnly,
            new SeamInsertionPath(SeamOrientation.Vertical, image.Size, [0]), []);
        Assert.Equal(SeamMaskValue.PreferRemoval, second.Mask.Get(1, 0));
    }

    [Fact]
    public void 影子规划返回互不重复的批次起点坐标且可逐条插入()
    {
        var image = CoordinateImage(4, 3);
        var mask = new SeamMask(image.Size);
        var calculator = SeamEnergyTests.Calculator();
        var remover = new SeamRemover();
        var planner = new SeamInsertionPlanner(calculator, new MinimumEnergySeamFinder(), remover);
        var batch = planner.Plan(image, mask, SeamOrientation.Vertical, 2);
        for (var row = 0; row < image.Size.Height; row++)
            Assert.NotEqual(batch.Paths[0].OriginalCoordinates[row], batch.Paths[1].OriginalCoordinates[row]);
        var inserter = new SeamInserter();
        var first = inserter.Insert(image, mask, batch.Paths[0], []);
        var second = inserter.Insert(first.Image, first.Mask, batch.Paths[1], [batch.Paths[0]]);
        Assert.Equal(new ImageSize(6, 3), second.Image.Size);
        Assert.Equal(6 * 3 * 4, second.Image.Rgba.Length);
    }

    [Fact]
    public void 水平边界插入使用唯一相邻像素且尺寸精确()
    {
        var image = CoordinateImage(2, 2);
        var path = new SeamInsertionPath(SeamOrientation.Horizontal, image.Size, [1, 1]);
        var result = new SeamInserter().Insert(image, new SeamMask(image.Size), path, []);
        Assert.Equal(new ImageSize(2, 3), result.Image.Size);
        Assert.Equal(((byte)10, (byte)20, (byte)1, (byte)255), result.Image.GetPixel(0, 2));
    }

    private static PixelImage CoordinateImage(int width, int height)
    {
        var bytes = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var offset = ((y * width) + x) * 4;
            bytes[offset] = (byte)(10 + x); bytes[offset + 1] = (byte)(20 + y);
            bytes[offset + 2] = (byte)((y * width) + x); bytes[offset + 3] = 255;
        }
        return new PixelImage(new ImageSize(width, height), bytes);
    }
}
