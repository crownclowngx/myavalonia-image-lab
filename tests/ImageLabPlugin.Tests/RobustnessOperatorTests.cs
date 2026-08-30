using ImageLabPlugin.Domain.Imaging;
using ImageLabPlugin.Domain.Robustness;
using ImageLabPlugin.Domain.Robustness.Operators;
using ImageLabPlugin.Domain.Watermarking;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class RobustnessOperatorTests
{
    [Fact]
    public async Task 高斯噪声同案例同种子逐字节一致且不修改源图()
    {
        var source = Image(32, 24); var original = source.Rgba.ToArray(); var op = new GaussianNoiseOperator(); var key = new RobustnessCaseKey(EmbeddingProfileId.Balanced, 0, 12m, 1);
        var first = await op.ApplyAsync(source, new GaussianNoiseParameters(12m), new(99, key, "noise", PerturbationKind.GaussianNoise), default);
        var second = await op.ApplyAsync(source, new GaussianNoiseParameters(12m), new(99, key, "noise", PerturbationKind.GaussianNoise), default);
        Assert.Equal(first.Rgba.ToArray(), second.Rgba.ToArray()); Assert.Equal(original, source.Rgba.ToArray()); Assert.NotEqual(original, first.Rgba.ToArray());
    }

    [Fact]
    public async Task 随机步骤子种子不依赖案例执行顺序()
    {
        var source = Image(16, 16); var op = new SaltPepperNoiseOperator();
        var a = new RobustnessCaseKey(EmbeddingProfileId.Stealth, 0, 0.1m, 0); var b = new RobustnessCaseKey(EmbeddingProfileId.Stealth, 1, 0.2m, 0);
        var a1 = await op.ApplyAsync(source, new SaltPepperParameters(.1m), new(7, a, "s", PerturbationKind.SaltPepperNoise), default);
        _ = await op.ApplyAsync(source, new SaltPepperParameters(.2m), new(7, b, "s", PerturbationKind.SaltPepperNoise), default);
        var a2 = await op.ApplyAsync(source, new SaltPepperParameters(.1m), new(7, a, "s", PerturbationKind.SaltPepperNoise), default);
        Assert.Equal(a1.Rgba.ToArray(), a2.Rgba.ToArray());
    }

    [Fact]
    public async Task 恒等参数逐字节不变且Alpha保持()
    {
        var source = Image(7, 5, alpha: 123); var key = new RobustnessCaseKey(EmbeddingProfileId.Balanced, 0, 0, 0);
        IImagePerturbationOperator[] operations = [new DeterministicPixelOperator(), new GaussianNoiseOperator(), new SaltPepperNoiseOperator(), new BrightnessOperator(), new ContrastOperator(), new GammaOperator(), new SaturationOperator(), new GaussianBlurOperator(), new UnsharpMaskOperator(), new ScaleOperator(), new CropOperator(), new PadOperator(), new TranslateOperator(), new RotateOperator(), new PerspectiveOperator(), new ColorBiasOperator()];
        PerturbationParameters[] parameters = [new DeterministicPixelParameters(), new GaussianNoiseParameters(), new SaltPepperParameters(), new BrightnessParameters(), new ContrastParameters(), new GammaParameters(), new SaturationParameters(), new GaussianBlurParameters(), new UnsharpMaskParameters(), new ScaleParameters(), new CropParameters(), new PadParameters(), new TranslateParameters(), new RotateParameters(), new PerspectiveParameters(), new ColorBiasParameters()];
        for (var i = 0; i < operations.Length; i++)
        {
            var output = await operations[i].ApplyAsync(source, parameters[i], new(1, key, $"s{i}", operations[i].Kind), default);
            Assert.Equal(source.Rgba.ToArray(), output.Rgba.ToArray());
        }
    }

    [Fact]
    public async Task 椒盐噪声修改精确比例且保持Alpha()
    {
        var source = Image(10, 10, alpha: 200); var op = new SaltPepperNoiseOperator(); var key = new RobustnessCaseKey(EmbeddingProfileId.Balanced, 0, .25m, 0);
        var output = await op.ApplyAsync(source, new SaltPepperParameters(.25m), new(5, key, "sp", op.Kind), default);
        var changed = 0; for (var i = 0; i < output.Rgba.Length; i += 4) { if (output.Rgba.Span[i] is 0 or 255) changed++; Assert.Equal(200, output.Rgba.Span[i + 3]); }
        Assert.Equal(25, changed);
    }

    [Fact]
    public async Task 缩放舍入和裁剪坐标方向固定()
    {
        var source = Image(3, 2); var key = new RobustnessCaseKey(EmbeddingProfileId.Balanced, 0, 1m, 0);
        var scaled = await new ScaleOperator().ApplyAsync(source, new ScaleParameters(1.5m, 1.5m), new(1, key, "scale", PerturbationKind.Scale), default);
        Assert.Equal(new ImageSize(5, 3), scaled.Size);
        var cropped = await new CropOperator().ApplyAsync(source, new CropParameters(1, 0, 0, 0), new(1, key, "crop", PerturbationKind.Crop), default);
        Assert.Equal(new ImageSize(2, 2), cropped.Size); Assert.Equal(source.GetPixel(1, 0), cropped.GetPixel(0, 0));
    }

    [Fact]
    public async Task 取消在行边界被观察()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await new MedianBlurOperator().ApplyAsync(Image(20, 20), new MedianBlurParameters(5), new(1, new(EmbeddingProfileId.Balanced, 0, 5, 0), "m", PerturbationKind.MedianBlur), cancellation.Token));
    }

    internal static PixelImage Image(int width, int height, byte alpha = 255)
    {
        var bytes = new byte[width * height * 4]; for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) { var o = (y * width + x) * 4; bytes[o] = (byte)(20 + x * 3); bytes[o + 1] = (byte)(40 + y * 4); bytes[o + 2] = 100; bytes[o + 3] = alpha; }
        return new(new(width, height), bytes);
    }
}
