using ImageLabPlugin.Domain.Frequency;
using ImageLabPlugin.Domain.FrequencyMaskEditing;
using Xunit;

namespace ImageLabPlugin.Tests;

/// <summary>坐标、共轭、自共轭、工具几何、锁定和强度公式的数值门禁。</summary>
public sealed class FrequencyMaskRasterizerTests
{
    private readonly FrequencyMaskRasterizer _rasterizer = new(new ConjugateMaskWriter());

    [Fact]
    public void 空配方逐值全通且对外数组不可修改()
    {
        var mask = _rasterizer.Rasterize(new FrequencyMaskRecipe(1), 8, 4);
        Assert.All(mask.Gains.ToArray(), value => Assert.Equal(1d, value));
        var copy = mask.Gains.ToArray(); copy[0] = 0;
        Assert.Equal(1d, mask[0, 0]);
    }

    [Fact]
    public void 普通频点与共轭点逐位相同()
    {
        var point = new NormalizedFrequencyPoint(5d / 7d, 4d / 7d);
        var mask = _rasterizer.Rasterize(new FrequencyMaskRecipe(1,
            [FrequencyMaskOperation.Brush([point], 0.001, 0, 0.5)]), 8, 8);
        var natural = FrequencyCoordinates.FromDisplay(5, 4, 8, 8);
        var conjugate = FrequencyCoordinates.ConjugateIndex(natural.InternalX, natural.InternalY, 8, 8);
        Assert.Equal(0.5, mask[natural.InternalX, natural.InternalY]);
        Assert.Equal(mask[natural.InternalX, natural.InternalY], mask[conjugate.X, conjugate.Y]);
    }

    [Fact]
    public void DC自共轭点只应用一次opacity()
    {
        var dcDisplay = new NormalizedFrequencyPoint(4d / 7d, 4d / 7d);
        var mask = _rasterizer.Rasterize(new FrequencyMaskRecipe(1,
            [FrequencyMaskOperation.Brush([dcDisplay], 0.001, 0, 0.5)]), 8, 8);
        Assert.Equal(0.5, mask[0, 0]);
    }

    [Fact]
    public void 重复Pointer点不改变单次gesture结果且稀疏路径被插值()
    {
        var start = new NormalizedFrequencyPoint(0.1, 0.5);
        var end = new NormalizedFrequencyPoint(0.9, 0.5);
        var sparse = _rasterizer.Rasterize(new FrequencyMaskRecipe(1,
            [FrequencyMaskOperation.Brush([start, end], 0.06, 0, 0.5)]), 32, 32);
        var repeated = _rasterizer.Rasterize(new FrequencyMaskRecipe(1,
            [FrequencyMaskOperation.Brush([start, start, end, end], 0.06, 0, 0.5)]), 32, 32);
        Assert.Equal(sparse.Gains.ToArray(), repeated.Gains.ToArray());
        for (var x = 5; x < 27; x++) Assert.True(sparse[FrequencyCoordinates.FromDisplay(x, 16, 32, 32).InternalX, 0] <= 1d);
    }

    [Fact]
    public void 橡皮向全通恢复而反转两次恢复原值()
    {
        var point = new NormalizedFrequencyPoint(4d / 7d, 4d / 7d);
        var operations = new[]
        {
            FrequencyMaskOperation.Brush([point], 0.001, 0, 1),
            FrequencyMaskOperation.Eraser([point], 0.001, 0.5),
            FrequencyMaskOperation.Invert(),
            FrequencyMaskOperation.Invert()
        };
        var mask = _rasterizer.Rasterize(new FrequencyMaskRecipe(1, operations), 8, 8);
        Assert.Equal(0.5, mask[0, 0]);
    }

    [Fact]
    public void 矩形边界包含且圆环中心孔保持全通()
    {
        var rectangle = _rasterizer.Rasterize(new FrequencyMaskRecipe(1,
            [FrequencyMaskOperation.Rectangle(new(0.25, 0.25), new(0.75, 0.75), 0, 1)]), 8, 8);
        Assert.Contains(rectangle.Gains.ToArray(), value => value == 0d);
        var ring = _rasterizer.Rasterize(new FrequencyMaskRecipe(1,
            [FrequencyMaskOperation.Ring(new(4d / 7d, 4d / 7d), 0.15, 0.35, 0, 1)]), 8, 8);
        Assert.Equal(1d, ring[0, 0]);
        Assert.Contains(ring.Gains.ToArray(), value => value == 0d);
    }

    [Fact]
    public void 频带锁定外保持全通且边界包含()
    {
        var band = new FrequencyBandLock(0.2, 0.45);
        var mask = _rasterizer.Rasterize(new FrequencyMaskRecipe(1,
            [FrequencyMaskOperation.Rectangle(new(0, 0), new(1, 1), 0, 1, band)]), 16, 16);
        Assert.Equal(1d, mask[0, 0]);
        for (var y = 0; y < 16; y++) for (var x = 0; x < 16; x++)
        {
            var radius = FrequencyCoordinates.FromInternal(x, y, 16, 16).Radius;
            if (radius < 0.2 || radius > 0.45) Assert.Equal(1d, mask[x, y]);
        }
    }

    [Theory]
    [InlineData(0d, 1d)]
    [InlineData(1d, 0.25d)]
    [InlineData(0.5d, 0.625d)]
    public void 全局强度公式固定(double strength, double expected)
    {
        var edit = new FrequencyGainMask(2, 1, [0.25, 0.25]);
        var effective = _rasterizer.CreateEffective(edit, strength);
        Assert.All(effective.Gains.ToArray(), value => Assert.Equal(expected, value, 12));
    }

    [Fact]
    public void 重置操作恢复全通且不受频带锁定影响()
    {
        var mask = _rasterizer.Rasterize(new FrequencyMaskRecipe(1,
            [FrequencyMaskOperation.Rectangle(new(0, 0), new(1, 1), 0, 1, new(0.2, 0.8)), FrequencyMaskOperation.Reset()]), 8, 8);
        Assert.All(mask.Gains.ToArray(), value => Assert.Equal(1d, value));
    }

    [Fact]
    public void 已取消重放不返回部分网格()
    {
        using var source = new CancellationTokenSource(); source.Cancel();
        Assert.Throws<OperationCanceledException>(() => _rasterizer.Rasterize(new FrequencyMaskRecipe(1,
            [FrequencyMaskOperation.Rectangle(new(0, 0), new(1, 1), 0, 1)]), 64, 64, source.Token));
    }
}
