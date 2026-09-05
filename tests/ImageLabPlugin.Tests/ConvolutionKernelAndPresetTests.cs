using ImageLabPlugin.Domain.Convolution;
using ImageLabPlugin.Domain.Shared.Spatial;
using Xunit;

namespace ImageLabPlugin.Tests;

/// <summary>固定核值对象、文本协议和全部有限预设的领域事实。</summary>
public sealed class ConvolutionKernelAndPresetTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(32)]
    public void 核拒绝偶数或越界尺寸(int size) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConvolutionKernel(size, new double[Math.Max(0, size * size)]));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(1024.01)]
    public void 核拒绝非有限或绝对值越界系数(double value)
    {
        var coefficients = new double[9]; coefficients[4] = value;
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConvolutionKernel(3, coefficients));
    }

    [Fact]
    public void 核复制输入且对外内存不能改变内部事实()
    {
        var source = new double[] { 0, 0, 0, 0, 1, 0, 0, 0, 0 };
        var kernel = new ConvolutionKernel(3, source); source[4] = 99;
        var exported = kernel.Coefficients.ToArray(); exported[4] = 88;
        Assert.Equal(1, kernel[1, 1]);
    }

    [Theory]
    [InlineData("1 0 0\n0 1 0\n0 0 1")]
    [InlineData("1,0,0\n0,1,0\n0,0,1")]
    [InlineData("1;0;0\n0;1;0\n0;0;1")]
    [InlineData("1\t0\t0\n0\t1\t0\n0\t0\t1")]
    public void 解析器接受四类列分隔符(string text)
    {
        var result = new ConvolutionKernelParser().Parse(text);
        Assert.True(result.IsSuccess); Assert.Equal(3, result.Kernel!.Size); Assert.Equal(3, result.Kernel.Sum);
    }

    [Theory]
    [InlineData("1 2\n3 4", 1, 1)]
    [InlineData("1 2 3\n4 x 6\n7 8 9", 2, 2)]
    [InlineData("1 2 3\n4 5\n7 8 9", 2, 3)]
    public void 解析错误带准确行列(string text, int row, int column)
    {
        var result = new ConvolutionKernelParser().Parse(text);
        Assert.False(result.IsSuccess); Assert.Equal(row, result.Errors[0].Row); Assert.Equal(column, result.Errors[0].Column);
    }

    [Fact]
    public void Gaussian对称非负且归一化()
    {
        var kernel = new ConvolutionPresetFactory().Gaussian(5, 1.2);
        Assert.Equal(1, kernel.Sum, 12);
        for (var row = 0; row < 5; row++) for (var column = 0; column < 5; column++)
        { Assert.True(kernel[row, column] >= 0); Assert.Equal(kernel[row, column], kernel[4 - row, 4 - column], 12); }
    }

    [Fact]
    public void Unsharp零强度精确等于单位核且HighBoost满足Dc公式()
    {
        var factory = new ConvolutionPresetFactory(); var identity = factory.Identity(5); var unsharp = factory.Unsharp(5, 1, 0);
        Assert.Equal(identity.Coefficients.ToArray(), unsharp.Coefficients.ToArray());
        Assert.Equal(2.5 - 1, factory.HighBoost(5, 1, 2.5).Sum, 12);
    }

    [Theory]
    [InlineData("sobel")]
    [InlineData("prewitt")]
    [InlineData("scharr")]
    public void 梯度预设提供两个零和核(string id)
    {
        var value = new ConvolutionPresetFactory().Create(id);
        Assert.Equal(ConvolutionOperatorKind.GradientPair, value.Kind);
        Assert.Equal(0, value.PrimaryKernel.Sum, 12); Assert.Equal(0, value.SecondaryKernel!.Sum, 12);
    }

    [Theory]
    [InlineData("laplacian-4")]
    [InlineData("laplacian-8")]
    public void Laplacian是零和并建议有符号显示偏置(string id)
    {
        var value = new ConvolutionPresetFactory().Create(id);
        Assert.Equal(0, value.PrimaryKernel.Sum, 12); Assert.Equal(128, value.RecommendedBias);
    }

    [Fact]
    public void Motion权重非负归一且重复生成确定()
    {
        var factory = new ConvolutionPresetFactory(); var first = factory.Motion(7, 5, -30); var second = factory.Motion(7, 5, 150);
        Assert.Equal(first.Coefficients.ToArray(), second.Coefficients.ToArray());
        Assert.All(first.Coefficients.ToArray(), value => Assert.True(value >= 0)); Assert.Equal(1, first.Sum, 12);
    }

    [Fact]
    public void Recipe指纹忽略显示名但响应数学参数变化()
    {
        var factory = new ConvolutionPresetFactory(); var definition = factory.Create("identity");
        var first = new ConvolutionRecipe(definition, new(BorderMode.Replicate), new(KernelNormalizationMode.None), 0, ConvolutionChannelMode.Red);
        var renamed = first with { Operator = definition with { DisplayName = "renamed" } };
        Assert.Equal(first.Fingerprint(), renamed.Fingerprint()); Assert.NotEqual(first.Fingerprint(), (first with { Bias = 1 }).Fingerprint());
    }
}
