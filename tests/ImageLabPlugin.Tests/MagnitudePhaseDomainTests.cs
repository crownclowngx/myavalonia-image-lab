using System.Numerics;
using ImageLabPlugin.Application.MagnitudePhaseSwap;
using ImageLabPlugin.Domain.Shared.Spectral;
using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Domain.MagnitudePhaseSwap;
using ImageLabPlugin.Plugin;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class MagnitudePhaseDomainTests
{
    [Theory]
    [InlineData(256)]
    [InlineData(512)]
    [InlineData(1024)]
    public void 规范画布只接受三档尺寸(int size) => MagnitudePhaseCanvasSize.Validate(size);

    [Theory]
    [InlineData(0)]
    [InlineData(128)]
    [InlineData(2048)]
    public void 规范画布拒绝未冻结尺寸(int size) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => MagnitudePhaseCanvasSize.Validate(size));

    [Fact]
    public void 透明像素先在白底合成且预览来自同一亮度事实()
    {
        var source = new PixelImage(new ImageSize(1, 1), new byte[] { 0, 0, 0, 0 });
        var canvas = new FrequencyPairCanvasProjector().Project(source, 256);
        Assert.All(canvas.Values.ToArray(), value => Assert.Equal(255d, value, 12));
        Assert.Equal((byte)255, canvas.CreatePreview().GetPixel(120, 80).R);
    }

    [Fact]
    public void FitContain保持比例并居中填白()
    {
        var source = Solid(2, 1, 0, 0, 0, 255);
        var canvas = new FrequencyPairCanvasProjector().Project(source, 256);
        Assert.Equal(new FrequencyPairContentRectangle(0, 64, 256, 128), canvas.Content);
        Assert.Equal(255d, canvas.Values.Span[0]);
        Assert.Equal(0d, canvas.Values.Span[(64 * 256) + 100], 10);
    }

    [Fact]
    public void 放大双线性使用像素中心并正确钳住边缘坐标()
    {
        var source = new PixelImage(new ImageSize(2, 1), new byte[] { 0, 0, 0, 255, 255, 255, 255, 255 });
        var canvas = new FrequencyPairCanvasProjector().Project(source, 256);
        var row = canvas.Content.Y + 10;
        Assert.Equal(0d, canvas.Values.Span[row * 256], 10);
        Assert.Equal(255d, canvas.Values.Span[(row * 256) + 255], 10);
        Assert.InRange(canvas.Values.Span[(row * 256) + 127], 126d, 128d);
    }

    [Fact]
    public void 缩小使用面积聚合而非单点抽样()
    {
        var bytes = new byte[512 * 512 * 4];
        for (var y = 0; y < 512; y++)
        for (var x = 0; x < 512; x++)
        {
            var level = ((x + y) & 1) == 0 ? (byte)0 : (byte)255;
            var offset = ((y * 512) + x) * 4;
            bytes[offset] = bytes[offset + 1] = bytes[offset + 2] = level; bytes[offset + 3] = 255;
        }
        var canvas = new FrequencyPairCanvasProjector().Project(new PixelImage(new ImageSize(512, 512), bytes), 256);
        Assert.InRange(canvas.Values.Span[0], 127.49d, 127.51d);
        Assert.All(canvas.Values.ToArray(), value => Assert.InRange(value, 127.49d, 127.51d));
    }

    [Fact]
    public void 内容指纹对相同输入稳定且对像素变化敏感()
    {
        var projector = new FrequencyPairCanvasProjector();
        var first = projector.Project(Solid(3, 2, 10, 20, 30, 255), 256);
        var second = projector.Project(Solid(3, 2, 10, 20, 30, 255), 256);
        var changed = projector.Project(Solid(3, 2, 11, 20, 30, 255), 256);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.NotEqual(first.Fingerprint, changed.Fingerprint);
    }

    [Fact]
    public void 两种交换按供体组合且精确写入共轭()
    {
        var a = EmptySpectrum(); var b = EmptySpectrum();
        a[1] = Complex.FromPolarCoordinates(2d, 0d); a[255] = Complex.Conjugate(a[1]);
        b[1] = Complex.FromPolarCoordinates(7d, Math.PI / 2d); b[255] = Complex.Conjugate(b[1]);
        var mixer = new SpectrumComponentMixer();
        var ab = mixer.Mix(Spectrum(a), Spectrum(b), new MagnitudePhaseRecipe(256,
            MagnitudeComponentMode.SourceA, 0d, PhaseComponentMode.SourceB, 0d,
            MagnitudePhaseProjectionKind.PhysicalClamp));
        Assert.Equal(2d, ab.OwnedSpectrum[1].Magnitude, 10);
        Assert.Equal(Math.PI / 2d, ab.OwnedSpectrum[1].Phase, 10);
        Assert.Equal(Complex.Conjugate(ab.OwnedSpectrum[1]), ab.OwnedSpectrum[255]);
        Assert.InRange(ab.Diagnostics.RelativeMagnitudeError, 0d, 1e-12);
        Assert.InRange(ab.Diagnostics.WeightedPhaseErrorRadians, 0d, 1e-12);

        var ba = mixer.Mix(Spectrum(a), Spectrum(b), new MagnitudePhaseRecipe(256,
            MagnitudeComponentMode.SourceB, 0d, PhaseComponentMode.SourceA, 0d,
            MagnitudePhaseProjectionKind.PhysicalClamp));
        Assert.Equal(7d, ba.OwnedSpectrum[1].Magnitude, 10);
        Assert.Equal(0d, ba.OwnedSpectrum[1].Phase, 10);
    }

    [Fact]
    public void 自共轭点始终为实数并遵循相位符号()
    {
        var a = EmptySpectrum(); var b = EmptySpectrum();
        a[128] = new Complex(3d, 0d); b[128] = new Complex(-9d, 0d);
        var result = new SpectrumComponentMixer().Mix(Spectrum(a), Spectrum(b), new MagnitudePhaseRecipe(256,
            MagnitudeComponentMode.SourceA, 0d, PhaseComponentMode.SourceB, 0d,
            MagnitudePhaseProjectionKind.PhysicalClamp));
        Assert.Equal(new Complex(-3d, 0d), result.OwnedSpectrum[128]);
        Assert.Equal(0d, result.Diagnostics.MaximumConjugateError);
    }

    [Fact]
    public void 最短圆弧跨正负Pi且固定正向Pi歧义()
    {
        var crossing = SpectrumComponentMixer.InterpolatePhase(170d * Math.PI / 180d,
            -170d * Math.PI / 180d, .5d, out var ambiguous);
        Assert.False(ambiguous);
        Assert.Equal(Math.PI, crossing, 10);
        var tie = SpectrumComponentMixer.InterpolatePhase(0d, -Math.PI, .5d, out ambiguous);
        Assert.True(ambiguous);
        Assert.Equal(Math.PI / 2d, tie, 10);
    }

    [Fact]
    public void 配方构造时拒绝半有效组合()
    {
        Assert.Throws<ArgumentException>(() => new MagnitudePhaseRecipe(256,
            MagnitudeComponentMode.SourceA, .5d, PhaseComponentMode.SourceB, 0d,
            MagnitudePhaseProjectionKind.PhysicalClamp));
        Assert.Throws<ArgumentException>(() => new MagnitudePhaseRecipe(256,
            MagnitudeComponentMode.UnitNonZero, 0d, PhaseComponentMode.SourceA, 0d,
            MagnitudePhaseProjectionKind.PhysicalClamp));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MagnitudePhaseRecipe(256,
            MagnitudeComponentMode.LinearAtoB, 1.1d, PhaseComponentMode.SourceA, 0d,
            MagnitudePhaseProjectionKind.PhysicalClamp));
        Assert.Throws<ArgumentException>(() => new MagnitudePhaseRecipe(256,
            MagnitudeComponentMode.LinearAtoB, .5d, PhaseComponentMode.ShortestArcAtoB, .5d,
            MagnitudePhaseProjectionKind.PhysicalClamp));
    }

    [Fact]
    public void Ifft归一化正确且虚部残差受门禁()
    {
        using var provider = Provider();
        var work = EmptySpectrum(); work[0] = new Complex(5d * 256d * 256d, 0d);
        var raw = provider.GetRequiredService<MagnitudePhaseReconstructor>().Reconstruct(work, 256);
        Assert.All(raw.Values.ToArray(), value => Assert.Equal(5d, value, 9));
        Assert.InRange(raw.MaximumImaginaryResidual, 0d, 1e-12);

        var invalid = EmptySpectrum(); invalid[1] = Complex.ImaginaryOne;
        Assert.Throws<InvalidDataException>(() => provider.GetRequiredService<MagnitudePhaseReconstructor>()
            .Reconstruct(invalid, 256));
    }

    [Fact]
    public void 物理投影统计裁切而科学投影固定零中心标签()
    {
        var values = Enumerable.Repeat(0d, 256 * 256).ToArray(); values[0] = -10d; values[1] = 300d; values[2] = 10d;
        var raw = new MagnitudePhaseRawResult(256, values, 0d, 0d);
        var projector = new MagnitudePhaseDisplayProjector();
        var physical = projector.Project(raw, MagnitudePhaseProjectionKind.PhysicalClamp);
        Assert.Equal(1, physical.Statistics.ClippedLowCount); Assert.Equal(1, physical.Statistics.ClippedHighCount);
        Assert.Equal((byte)0, physical.Image.GetPixel(0, 0).R); Assert.Equal((byte)255, physical.Image.GetPixel(1, 0).R);
        var scientific = projector.Project(raw, MagnitudePhaseProjectionKind.SignedScientific);
        Assert.Equal("诊断显示，不保留原亮度量纲", scientific.DiagnosticLabel);
        Assert.Equal((byte)128, scientific.Image.GetPixel(3, 0).R);
    }

    [Fact]
    public void 工作集估算在分配前使用受控上限()
    {
        var estimator = new MagnitudePhaseResourceEstimator();
        Assert.InRange(estimator.EstimateBytes(1024), 1, MagnitudePhaseResourceEstimator.MaximumEstimatedBytes);
        Assert.Throws<ArgumentOutOfRangeException>(() => estimator.EstimateBytes(2048));
    }

    private static ServiceProvider Provider()
    {
        var services = new ServiceCollection(); services.AddImageLabPluginServices();
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static Complex[] EmptySpectrum() => new Complex[256 * 256];
    private static FrequencySpectrum Spectrum(Complex[] values) =>
        new(new ImageSize(256, 256), 256, 256, values);

    private static PixelImage Solid(int width, int height, byte r, byte g, byte b, byte a)
    {
        var bytes = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++)
        { bytes[i * 4] = r; bytes[(i * 4) + 1] = g; bytes[(i * 4) + 2] = b; bytes[(i * 4) + 3] = a; }
        return new PixelImage(new ImageSize(width, height), bytes);
    }
}
