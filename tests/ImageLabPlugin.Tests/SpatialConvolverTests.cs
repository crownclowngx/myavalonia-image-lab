using ImageLabPlugin.Domain.Convolution;
using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Domain.Shared.Spatial;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class SpatialConvolverTests
{
    [Theory]
    [InlineData(0, -7, 0, true)]
    [InlineData(1, -7, 0, false)]
    [InlineData(1, 8, 2, false)]
    [InlineData(2, -7, 1, false)]
    [InlineData(2, 8, 0, false)]
    [InlineData(3, -7, 2, false)]
    [InlineData(3, 8, 2, false)]
    public void 边界映射支持负正多周期(int modeValue, int index, int expected, bool constant)
    {
        var mapped = BorderIndexMapper.Map(index, 3, (BorderMode)modeValue);
        Assert.Equal(expected, mapped.Index); Assert.Equal(constant, mapped.IsConstant);
    }

    [Theory]
    [InlineData(-100)] [InlineData(-1)] [InlineData(0)] [InlineData(100)]
    public void Reflect101长度一总是映射零(int index) => Assert.Equal(0, BorderIndexMapper.Map(index, 1, BorderMode.Reflect101).Index);

    [Fact]
    public void 非对称Impulse证明执行的是真卷积而非相关()
    {
        var source = new double[25]; source[(2 * 5) + 2] = 1;
        var kernel = new ConvolutionKernel(3, [0, 0, 0, 0, 0, 2, 0, 0, 0]);
        var result = Execute(source, 5, 5, kernel);
        Assert.Equal(2, result[3, 2]); Assert.Equal(0, result[1, 2]);
    }

    [Fact]
    public void 核大于图片仍按Wrap多周期采样()
    {
        var kernel = new ConvolutionPresetFactory().Mean(5);
        var result = new SpatialConvolver().Convolve([10d, 20d], 2, 1, kernel, new(BorderMode.Wrap), new(KernelNormalizationMode.KernelSum), 0);
        Assert.Equal(14, result[0, 0], 10); Assert.Equal(16, result[1, 0], 10);
    }

    [Fact]
    public void 归一化和显式除数按定义应用()
    {
        var kernel = new ConvolutionKernel(3, [0, 0, 0, 0, 2, 0, 0, 0, 0]);
        var sum = new SpatialConvolver().Convolve([7d], 1, 1, kernel, new(BorderMode.Replicate), new(KernelNormalizationMode.KernelSum), 0);
        var explicitResult = new SpatialConvolver().Convolve([7d], 1, 1, kernel, new(BorderMode.Replicate), new(KernelNormalizationMode.Explicit, 4), 0);
        Assert.Equal(7, sum[0, 0]); Assert.Equal(3.5, explicitResult[0, 0]);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)]
    public void 零核归一化在执行前阻断(int modeValue)
    {
        var kernel = new ConvolutionKernel(3, new double[9]);
        Assert.Throws<ArgumentOutOfRangeException>(() => Execute([1d], 1, 1, kernel, (KernelNormalizationMode)modeValue));
    }

    [Fact]
    public void AwayFromZero舍入偏置和两端裁切都有统计()
    {
        var kernel = new ConvolutionPresetFactory().Identity(3);
        var result = new SpatialConvolver().Convolve([-1.5, 1.5, 300], 3, 1, kernel, new(BorderMode.Replicate), new(KernelNormalizationMode.None), 0);
        Assert.Equal(new byte[] { 0, 2, 255 }, result.Bytes.ToArray());
        Assert.Equal(1, result.Statistics.LowClippedSamples); Assert.Equal(1, result.Statistics.HighClippedSamples);
        Assert.Equal(-1.5, result.Statistics.RawMinimum); Assert.Equal(300, result.Statistics.RawMaximum);
    }

    [Fact]
    public void 执行不改变输入且预取消不返回半成品()
    {
        var source = Enumerable.Range(0, 100).Select(value => (double)value).ToArray(); var clone = source.ToArray();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => new SpatialConvolver().Convolve(source, 10, 10,
            new ConvolutionPresetFactory().Mean(31), new(BorderMode.Wrap), new(KernelNormalizationMode.KernelSum), 0, cancellation.Token));
        Assert.Equal(clone, source);
    }

    [Fact]
    public void RGB分别处理且Alpha逐字节保持()
    {
        var image = new PixelImage(new ImageSize(2, 1), [10, 20, 30, 4, 40, 50, 60, 8]);
        var factory = new ConvolutionPresetFactory(); var recipe = new ConvolutionRecipe(factory.Create("identity"), new(BorderMode.Replicate), new(KernelNormalizationMode.None), 1, ConvolutionChannelMode.Rgb);
        var output = new ConvolutionImageProcessor(new ImageChannelConverter(), new SpatialConvolver(), new GradientCombiner()).Process(image, recipe);
        Assert.Equal(new byte[] { 11, 21, 31, 4, 41, 51, 61, 8 }, output.Image.Rgba.ToArray()); Assert.Equal(3, output.Channels.Count);
    }

    [Fact]
    public void 单R通道不改变其他颜色与Alpha()
    {
        var image = new PixelImage(new ImageSize(1, 1), [10, 20, 30, 40]); var factory = new ConvolutionPresetFactory();
        var recipe = new ConvolutionRecipe(factory.Create("identity"), new(BorderMode.Replicate), new(KernelNormalizationMode.None), 5, ConvolutionChannelMode.Red);
        var output = new ConvolutionImageProcessor(new ImageChannelConverter(), new SpatialConvolver(), new GradientCombiner()).Process(image, recipe);
        Assert.Equal(new byte[] { 15, 20, 30, 40 }, output.Image.Rgba.ToArray());
    }

    [Fact]
    public void Magnitude只在组合后应用一次偏置()
    {
        var source = new PixelImage(new ImageSize(3, 1), [0, 0, 0, 255, 10, 10, 10, 255, 20, 20, 20, 255]);
        var factory = new ConvolutionPresetFactory(); var recipe = new ConvolutionRecipe(factory.Create("prewitt"), new(BorderMode.Replicate), new(KernelNormalizationMode.None), 7, ConvolutionChannelMode.Red, GradientOutputMode.Magnitude);
        var output = new ConvolutionImageProcessor(new ImageChannelConverter(), new SpatialConvolver(), new GradientCombiner()).Process(source, recipe);
        Assert.Equal(67, output.Image.GetPixel(1, 0).R);
    }

    private static ConvolutionPlaneResult Execute(double[] source, int width, int height, ConvolutionKernel kernel,
        KernelNormalizationMode mode = KernelNormalizationMode.None) =>
        new SpatialConvolver().Convolve(source, width, height, kernel, new(BorderMode.Replicate), new(mode), 0);
}
