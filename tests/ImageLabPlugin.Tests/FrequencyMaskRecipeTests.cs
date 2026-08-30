using System.Text;
using System.Text.Json.Nodes;
using ImageLabPlugin.Domain.FrequencyMaskEditing;
using ImageLabPlugin.Infrastructure.Persistence;
using Xunit;

namespace ImageLabPlugin.Tests;

/// <summary>不可变配方、有界历史、规范指纹和严格 schema 的门禁。</summary>
public sealed class FrequencyMaskRecipeTests
{
    [Theory]
    [InlineData(-0.1, 0.5)]
    [InlineData(1.1, 0.5)]
    [InlineData(0.5, -0.1)]
    [InlineData(0.5, 1.1)]
    public void 归一化坐标拒绝越界(double x, double y) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new NormalizedFrequencyPoint(x, y));

    [Fact]
    public void 操作防御性复制路径且校验几何与有限值()
    {
        var points = new[] { new NormalizedFrequencyPoint(0.1, 0.2), new NormalizedFrequencyPoint(0.8, 0.9) };
        var operation = FrequencyMaskOperation.Brush(points, 0.05, 0.2, 0.8);
        points[0] = new NormalizedFrequencyPoint(0.9, 0.9);
        Assert.Equal(0.1, operation.Points[0].X);
        Assert.Throws<ArgumentException>(() => FrequencyMaskOperation.Rectangle(new(0.5, 0.1), new(0.5, 0.9), 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => FrequencyMaskOperation.Ring(new(0.5, 0.5), 0.5, 0.5, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => FrequencyMaskOperation.Brush([new(0.5, 0.5)], 0.1, double.NaN, 1));
    }

    [Fact]
    public void 相同配方指纹稳定而顺序强度和几何变化会改变指纹()
    {
        var a = FrequencyMaskOperation.Brush([new(0.2, 0.3)], 0.1, 0, 1);
        var b = FrequencyMaskOperation.Rectangle(new(0.1, 0.1), new(0.4, 0.4), 0.5, 1);
        var first = new FrequencyMaskRecipe(1, [a, b], 8, 8);
        Assert.Equal(first.Fingerprint(), new FrequencyMaskRecipe(1, [a, b], 8, 8).Fingerprint());
        Assert.NotEqual(first.Fingerprint(), new FrequencyMaskRecipe(0.5, [a, b], 8, 8).Fingerprint());
        Assert.NotEqual(first.Fingerprint(), new FrequencyMaskRecipe(1, [b, a], 8, 8).Fingerprint());
    }

    [Fact]
    public void 历史支持撤销重做且新编辑清空redo()
    {
        var history = new MaskEditHistory();
        history.Add(FrequencyMaskOperation.Invert());
        history.Add(FrequencyMaskOperation.Reset());
        Assert.True(history.Undo());
        Assert.True(history.CanRedo);
        Assert.Single(history.CreateRecipe(1).Operations);
        Assert.True(history.Redo());
        Assert.Equal(2, history.CreateRecipe(1).Operations.Count);
        Assert.True(history.Undo());
        history.Add(FrequencyMaskOperation.Invert());
        Assert.False(history.CanRedo);
        Assert.Equal(2, history.TotalStoredCount);
    }

    [Fact]
    public void 历史达到操作上限时先阻断且不丢旧工作()
    {
        var history = new MaskEditHistory();
        for (var i = 0; i < FrequencyMaskRecipe.MaximumOperations; i++) history.Add(FrequencyMaskOperation.Invert());
        Assert.Throws<InvalidOperationException>(() => history.Add(FrequencyMaskOperation.Reset()));
        Assert.Equal(FrequencyMaskRecipe.MaximumOperations, history.Count);
    }

    [Fact]
    public void Schema1配方可往返且保留操作与频带锁定()
    {
        var serializer = new FrequencyMaskRecipeSerializer();
        var band = new FrequencyBandLock(0.2, 0.8);
        var recipe = new FrequencyMaskRecipe(0.75,
            [FrequencyMaskOperation.Brush([new(0.1, 0.2), new(0.4, 0.5)], 0.04, 0.3, 0.7, band), FrequencyMaskOperation.Invert()], 16, 8);
        var restored = serializer.Deserialize(serializer.Serialize(recipe));
        Assert.Equal(recipe.Fingerprint(), restored.Fingerprint());
        Assert.Equal(2, restored.Operations.Count);
        Assert.Equal(band, restored.Operations[0].BandLock);
    }

    [Fact]
    public void 未知kind和篡改指纹完整拒绝()
    {
        var serializer = new FrequencyMaskRecipeSerializer();
        var recipe = new FrequencyMaskRecipe(1, [FrequencyMaskOperation.Invert()]);
        var root = JsonNode.Parse(serializer.Serialize(recipe))!.AsObject();
        root["operations"]![0]!["kind"] = "unknown";
        Assert.Throws<InvalidDataException>(() => serializer.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString())));

        root = JsonNode.Parse(serializer.Serialize(recipe))!.AsObject();
        root["fingerprint"] = "0000000000000000";
        Assert.Throws<InvalidDataException>(() => serializer.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString())));
    }

    [Fact]
    public void 未知字段和超限JSON被拒绝()
    {
        var serializer = new FrequencyMaskRecipeSerializer();
        var root = JsonNode.Parse(serializer.Serialize(new FrequencyMaskRecipe(1)))!.AsObject();
        root["unexpected"] = true;
        Assert.Throws<InvalidDataException>(() => serializer.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString())));
        Assert.Throws<InvalidDataException>(() => serializer.Deserialize(new byte[(1024 * 1024) + 1]));
    }
}
