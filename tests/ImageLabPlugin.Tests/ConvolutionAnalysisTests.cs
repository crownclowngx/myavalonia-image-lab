using ImageLabPlugin.Domain.Convolution;
using ImageLabPlugin.Domain.Frequency;
using ImageLabPlugin.Domain.Imaging;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class ConvolutionAnalysisTests
{
    private static KernelFrequencyResponseAnalyzer CreateAnalyzer() => new(new Fft2DTransform(new Fft1DTransform()));

    [Fact]
    public void Identity频响全幅值一且Dc为一()
    {
        var recipe = Recipe(new ConvolutionPresetFactory().Create("identity"), KernelNormalizationMode.None);
        var response = CreateAnalyzer().Analyze(recipe);
        Assert.Equal(1, response.DcGain, 10); Assert.All(response.Magnitudes.ToArray(), value => Assert.Equal(1, value, 9));
        Assert.All(response.Phases.ToArray(), value => Assert.Equal(0, value, 9));
    }

    [Theory]
    [InlineData("mean", 1, 1d)]
    [InlineData("gaussian", 1, 1d)]
    [InlineData("laplacian-4", 0, 0d)]
    [InlineData("sobel", 0, 0d)]
    public void Dc增益符合核和公式(string id, int modeValue, double expected)
    {
        var response = CreateAnalyzer().Analyze(Recipe(new ConvolutionPresetFactory().Create(id), (KernelNormalizationMode)modeValue));
        Assert.Equal(expected, response.DcGain, 9);
    }

    [Fact]
    public void Bias不改变频响而除数按比例缩放()
    {
        var definition = new ConvolutionPresetFactory().Create("identity");
        var first = CreateAnalyzer().Analyze(Recipe(definition, KernelNormalizationMode.Explicit, 2, bias: 0));
        var biased = CreateAnalyzer().Analyze(Recipe(definition, KernelNormalizationMode.Explicit, 2, bias: 100));
        var scaled = CreateAnalyzer().Analyze(Recipe(definition, KernelNormalizationMode.Explicit, 4, bias: 0));
        Assert.Equal(first.Magnitudes.ToArray(), biased.Magnitudes.ToArray()); Assert.Equal(first.DcGain / 2, scaled.DcGain, 12);
    }

    [Fact]
    public void 双核返回组合幅频摘要而不是虚构相位()
    {
        var response = CreateAnalyzer().Analyze(Recipe(new ConvolutionPresetFactory().Create("scharr"), KernelNormalizationMode.None));
        Assert.True(response.IsGradientSummary); Assert.Equal(0, response.DcGain, 9); Assert.True(response.MaximumMagnitude > 0);
    }

    [Fact]
    public void 零差异投影为全黑绝对图和中性灰有符号图()
    {
        var image = new PixelImage(new ImageSize(1, 1), [10, 20, 30, 40]);
        var result = new ConvolutionDifferenceProjector().Project(image, image, 4);
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, result.Absolute.Rgba.ToArray());
        Assert.Equal(new byte[] { 128, 128, 128, 255 }, result.Signed.Rgba.ToArray()); Assert.Equal(0, result.ChangedPixels);
    }

    [Fact]
    public void 探针贡献和等于执行器累加且报告Constant来源()
    {
        var source = new PixelImage(new ImageSize(1, 1), [10, 20, 30, 255]);
        var plane = new double[] { 10 }; var kernel = new ConvolutionPresetFactory().Mean(3); var border = new BorderDefinition(BorderMode.Constant, 2);
        var normalization = new KernelNormalizationDefinition(KernelNormalizationMode.KernelSum);
        var result = new SpatialConvolver().Convolve(plane, 1, 1, kernel, border, normalization, 0);
        var output = new PixelImage(new ImageSize(1, 1), [result.Bytes.Span[0], 20, 30, 255]);
        var report = new ConvolutionPixelInspector().Inspect(source, output, plane, kernel, border, normalization, 0, 0, 0);
        Assert.Equal(report.Accumulator, report.Contributions.Sum(value => value.Product), 12);
        Assert.Equal(result[0, 0], report.DividedValue, 12); Assert.Equal(8, report.Contributions.Count(value => value.IsConstant));
    }

    [Fact]
    public void 差异统计计算MaeRmse最大值和变化像素()
    {
        var first = new PixelImage(new ImageSize(1, 1), [0, 10, 20, 255]);
        var second = new PixelImage(new ImageSize(1, 1), [3, 14, 20, 255]);
        var value = new ConvolutionDifferenceProjector().Project(first, second, 1);
        Assert.Equal(7d / 3d, value.MeanAbsoluteError, 12); Assert.Equal(Math.Sqrt(25d / 3d), value.RootMeanSquareError, 12);
        Assert.Equal(4, value.MaximumAbsoluteDifference); Assert.Equal(1, value.ChangedPixels);
    }

    [Fact]
    public void 双核探针分别保留两套贡献且Magnitude手算一致()
    {
        var source = new PixelImage(new ImageSize(3, 3), Enumerable.Repeat(new byte[] { 0, 0, 0, 255 }, 9).SelectMany(x => x).ToArray());
        var result = source.Clone(); var plane = Enumerable.Range(0, 9).Select(x => (double)x).ToArray();
        var gradient = new ConvolutionPresetFactory().Create("prewitt");
        var report = new ConvolutionPixelInspector().InspectGradient(source, result, plane, gradient.PrimaryKernel,
            gradient.SecondaryKernel!, new(BorderMode.Replicate), new(KernelNormalizationMode.None), 5, 1, 1);
        Assert.NotNull(report.SecondaryContributions); Assert.Equal(6, report.Contributions.Count); Assert.Equal(6, report.SecondaryContributions!.Count);
        Assert.Equal(Math.Sqrt((report.DividedValue * report.DividedValue) + (report.SecondaryDividedValue!.Value * report.SecondaryDividedValue.Value)), report.Magnitude!.Value, 12);
        Assert.Equal(Math.Round(report.Magnitude.Value + 5, MidpointRounding.AwayFromZero), report.RoundedValue);
    }

    private static ConvolutionRecipe Recipe(ConvolutionOperatorDefinition definition, KernelNormalizationMode mode,
        double divisor = 1, double bias = 0) => new(definition, new(BorderMode.Replicate), new(mode, divisor), bias, ConvolutionChannelMode.Red);
}
