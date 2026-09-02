using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Domain.PoissonBlending;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class PoissonColorAndGuidanceTests
{
    [Theory]
    [InlineData(0, 0d)]
    [InlineData(255, 1d)]
    [InlineData(128, 0.21586050011389926)]
    public void sRGB字节在线性光中有冻结Golden(byte value, double expected)
    {
        var color = new SrgbColorSpace(); var linear = color.Decode(SrgbColor.FromBytes(value, value, value));
        Assert.Equal(expected, linear.Red, 14); Assert.Equal(value, color.Encode(linear).ToBytes().Red);
    }

    [Fact]
    public void 普通克隆使用源梯度且反向边反对称()
    {
        var strategy = new NormalCloneGuidanceStrategy(); var a = new LinearRgbColor(.8, .4, .1); var b = new LinearRgbColor(.2, .1, .3);
        var forward = strategy.Evaluate(a, b, default, default); var reverse = strategy.Evaluate(b, a, default, default);
        Assert.Equal(.6, forward.C0, 14); Assert.Equal(-forward.C0, reverse.C0, 14); Assert.True(forward.SelectedSource);
    }

    [Fact]
    public void 混合梯度按整RGB向量择强且平局选源()
    {
        var strategy = new MixedGradientGuidanceStrategy(); var zero = new LinearRgbColor(0, 0, 0);
        var selectedTarget = strategy.Evaluate(new(.6, 0, 0), zero, new(.5, .5, 0), zero);
        Assert.False(selectedTarget.SelectedSource); Assert.Equal(.5, selectedTarget.C0); Assert.Equal(.5, selectedTarget.C1);
        var tie = strategy.Evaluate(new(.5, 0, 0), zero, new(0, .5, 0), zero);
        Assert.True(tie.SelectedSource); Assert.Equal(.5, tie.C0); Assert.Equal(0, tie.C1);
    }

    [Theory]
    [InlineData(1, 0, 0, 0.2126)]
    [InlineData(0, 1, 0, 0.7152)]
    [InlineData(0, 0, 1, 0.0722)]
    public void 单色模式使用线性BT709(double r, double g, double b, double expected)
    { Assert.Equal(expected, MonochromeGuidanceStrategy.Luma(new(r, g, b)), 14); }

    [Fact]
    public void Catalog拒绝缺失和重复Strategy()
    {
        Assert.Throws<InvalidOperationException>(() => new PoissonGuidanceCatalog([new NormalCloneGuidanceStrategy()]));
        Assert.Throws<InvalidOperationException>(() => new PoissonGuidanceCatalog([new NormalCloneGuidanceStrategy(), new NormalCloneGuidanceStrategy(), new MixedGradientGuidanceStrategy(), new MonochromeGuidanceStrategy()]));
    }
}
