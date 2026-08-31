using ImageLabPlugin.Domain.Imaging;
using ImageLabPlugin.Domain.SeamCarving;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class MinimumEnergySeamTests
{
    [Fact]
    public void 垂直平局选择最小前驱与最小终点()
    {
        var map = Map(3, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        var path = new MinimumEnergySeamFinder().Find(map, new SeamMask(map.Size), SeamOrientation.Vertical);
        Assert.Equal([0, 0, 0], path.Coordinates.ToArray());
    }

    [Fact]
    public void 水平路径使用列为主轴且不需要转置图片()
    {
        var map = Map(3, 2, 0.9, 0, 0.9, 0.1, 0.1, 0.1);
        var path = new MinimumEnergySeamFinder().Find(map, new SeamMask(map.Size), SeamOrientation.Horizontal);
        Assert.Equal([1, 0, 1], path.Coordinates.ToArray());
        Assert.Equal(0.2d, path.EffectiveEnergy, 12);
    }

    [Fact]
    public void 保护区绕行并准确统计命中()
    {
        var map = Map(3, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        var mask = new SeamMask(map.Size);
        for (var y = 0; y < 3; y++) mask.Set(0, y, SeamMaskValue.Protect);
        map = ApplyMask(map, mask);
        var path = new MinimumEnergySeamFinder().Find(map, mask, SeamOrientation.Vertical);
        Assert.Equal([1, 1, 1], path.Coordinates.ToArray());
        Assert.Equal(0, path.ProtectHits);
    }

    [Fact]
    public void 优先删除走廊优先且命中数准确()
    {
        var baseMap = Map(3, 3, 1, 1, 1, 1, 1, 1, 1, 1, 1);
        var mask = new SeamMask(baseMap.Size);
        for (var y = 0; y < 3; y++) mask.Set(2, y, SeamMaskValue.PreferRemoval);
        var path = new MinimumEnergySeamFinder().Find(ApplyMask(baseMap, mask), mask, SeamOrientation.Vertical);
        Assert.Equal([2, 2, 2], path.Coordinates.ToArray());
        Assert.Equal(3, path.PreferRemovalHits);
    }

    [Theory]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 4)]
    [InlineData(5, 5)]
    public void 动态规划与穷举全局最优一致(int width, int height)
    {
        var random = new Random(1234 + width);
        var values = Enumerable.Range(0, width * height).Select(_ => random.Next(0, 5) / 4d).ToArray();
        var map = Map(width, height, values);
        var path = new MinimumEnergySeamFinder().Find(map, new SeamMask(map.Size), SeamOrientation.Vertical);
        var bruteForce = EnumerateVertical(map).Min(item => item.Cost);
        Assert.Equal(bruteForce, path.EffectiveEnergy, 12);
    }

    [Fact]
    public void 路径拒绝错误长度越界与断裂()
    {
        var size = new ImageSize(3, 3);
        Assert.Throws<ArgumentException>(() => new SeamPath(SeamOrientation.Vertical, size, [0, 0], 0, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SeamPath(SeamOrientation.Vertical, size, [0, 3, 2], 0, 0, 0, 0));
        Assert.Throws<ArgumentException>(() => new SeamPath(SeamOrientation.Vertical, size, [0, 2, 1], 0, 0, 0, 0));
    }

    [Fact]
    public void 已取消Token在逐主轴边界可观察()
    {
        var map = Map(3, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => new MinimumEnergySeamFinder().Find(
            map, new SeamMask(map.Size), SeamOrientation.Vertical, cancellation.Token));
    }

    private static SeamEnergyMap Map(int width, int height, params double[] values)
    {
        var size = new ImageSize(width, height);
        return new SeamEnergyMap(size, values, values, new(0, 1, 0, 0, 0, 0));
    }

    private static SeamEnergyMap ApplyMask(SeamEnergyMap source, SeamMask mask)
    {
        var effective = source.BaseEnergy.ToArray();
        for (var y = 0; y < source.Size.Height; y++)
        for (var x = 0; x < source.Size.Width; x++)
        {
            var index = (y * source.Size.Width) + x;
            effective[index] += mask.Get(x, y) switch
            { SeamMaskValue.Protect => 1000, SeamMaskValue.PreferRemoval => -1000, _ => 0 };
        }
        return new(source.Size, source.BaseEnergy.Span, effective, source.Summary);
    }

    private static IEnumerable<(int[] Path, double Cost)> EnumerateVertical(SeamEnergyMap map)
    {
        var path = new int[map.Size.Height];
        IEnumerable<(int[], double)> Visit(int row)
        {
            if (row == map.Size.Height)
            {
                var copy = (int[])path.Clone();
                yield return (copy, copy.Select((x, y) => map.GetEffective(x, y)).Sum());
                yield break;
            }
            var minimum = row == 0 ? 0 : Math.Max(0, path[row - 1] - 1);
            var maximum = row == 0 ? map.Size.Width - 1 : Math.Min(map.Size.Width - 1, path[row - 1] + 1);
            for (var x = minimum; x <= maximum; x++)
            { path[row] = x; foreach (var item in Visit(row + 1)) yield return item; }
        }
        return Visit(0);
    }
}
