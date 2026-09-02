using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Application.Robustness;
using ImageLabPlugin.Application.Watermarking;
using ImageLabPlugin.Domain.Shared.Spectral;
using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Domain.Robustness;
using ImageLabPlugin.Domain.Shared.Perturbations;
using ImageLabPlugin.Domain.Watermarking;
using ImageLabPlugin.Domain.Shared.Analysis;
using ImageLabPlugin.Infrastructure.ErrorCorrection;
using ImageLabPlugin.Infrastructure.Robustness;
using ImageLabPlugin.Infrastructure.Perturbations;
using ImageLabPlugin.Infrastructure.Watermarking;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class RobustnessDiagnosticsAndChainTests
{
    [Fact]
    public void 人工副本翻转与投票翻转的BER精确匹配()
    {
        byte[] expected = [0b1010_0000];
        bool[] physical = [true, true, false, false, true, true, false, false, false, false, false, false, false, false, false, false];
        // redundancy=2：第 1 个预期 bit 的一个副本被翻转，投票字节另翻转最低两位。
        physical[1] = false; byte[] voted = [0b1010_0011];
        var result = ChannelBerCalculator.Compare(physical, voted, expected, 2);
        Assert.Equal(new BerMeasurement(1, 16), result.Physical); Assert.Equal(new BerMeasurement(2, 8), result.Voted);
    }

    [Fact]
    public async Task 链严格遵守顺序并记录失败后恢复()
    {
        var executor = new PerturbationChainExecutor([new BrightnessOperator(), new ContrastOperator()]); var source = Solid(2, 1, 100);
        var planned = new RobustnessPlannedCase(new(RobustnessProfileId.Balanced, 0, 1, 0),
            [new("b", PerturbationKind.Brightness, true, new BrightnessParameters(10)), new("c", PerturbationKind.Contrast, true, new ContrastParameters(2))]);
        var probes = 0;
        var result = await executor.ExecuteAsync(source, planned, 1, true, (_, _, _) =>
        {
            probes++; return new(probes == 2, RobustnessDetectionStatus.UnrecoverableDamage, RobustnessIntegrityStatus.Invalid, false, null, null, probes == 2 ? RobustnessFailureReason.None : RobustnessFailureReason.DataUnrecoverable, "test");
        }, default);
        Assert.Equal(93, result.Image.GetPixel(0, 0).R); Assert.Equal("b", result.FirstFailureStep); Assert.True(result.RecoveredAfterFailure);
    }

    [Fact]
    public void 重复Strategy显式拒绝且组合根覆盖每一种Kind()
    {
        Assert.Throws<InvalidOperationException>(() => new PerturbationChainExecutor([new BrightnessOperator(), new BrightnessOperator()]));
        IImagePerturbationOperator[] all = [new DeterministicPixelOperator(), new GaussianNoiseOperator(), new SaltPepperNoiseOperator(), new BrightnessOperator(), new ContrastOperator(), new GammaOperator(), new SaturationOperator(), new ColorBiasOperator(), new GaussianBlurOperator(), new MedianBlurOperator(), new UnsharpMaskOperator(), new ScaleOperator(), new CropOperator(), new PadOperator(), new TranslateOperator(), new RotateOperator(), new PerspectiveOperator(), new JpegReencodeOperator(new ThrowCodec())];
        Assert.Equal(Enum.GetValues<PerturbationKind>().Order(), all.Select(value => value.Kind).Order());
    }

    [Fact]
    public void 未扰动受控基线两类BER和分层RS均为零()
    {
        var rs = new ReedSolomonCodec(); var protocol = new WatermarkFrameProtocol(rs, new FixedRandom()); var carrier = new FrequencyWatermarkCarrier(new Dct8x8Transform(), protocol, rs);
        using var payload = new WatermarkPayload("ok"u8.ToArray(), PayloadContentType.Text); var frame = protocol.Encode(payload, EmbeddingProfileId.Balanced, null); var image = Textured(256, 256); var embedded = carrier.Embed(image, frame, default);
        var extractor = new ExtractWatermarkUseCase(new ThrowCodec(), carrier, protocol); using var baseline = new ControlledWatermarkBaseline(EmbeddingProfileId.Balanced, embedded, frame, extractor.Extract(embedded, null, default));
        var result = new WatermarkDiagnosticReader(carrier, protocol, extractor).Read(embedded, baseline, payload.Bytes.Span, null, default);
        Assert.True(result.Success); Assert.Equal(0, result.Header!.PhysicalRawBer.ErrorBits); Assert.Equal(0, result.Header.VotedPreEccBer.ErrorBits); Assert.Equal(0, result.Data!.PhysicalRawBer.ErrorBits); Assert.Equal(0, result.Data.VotedPreEccBer.ErrorBits); Assert.Equal(0, result.Header.CorrectedSymbols); Assert.Equal(0, result.Data.CorrectedSymbols);
    }

    [Fact]
    public async Task 取消返回不完整Session且没有伪失败案例()
    {
        var image = Solid(1, 1, 10); using var baseline = new RobustnessBaselineSession("a.png", image, [], [], new Dictionary<EmbeddingProfileId, ControlledWatermarkBaseline>(), "digest");
        var recipe = RobustnessRecipeTests.Recipe(new ExplicitValueScan([1m])); var plan = new RobustnessExperimentPlanner(new()).Plan(recipe, [RobustnessProfileId.Balanced]);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var useCase = new RunRobustnessExperimentUseCase(new PerturbationChainExecutor([]), new NeverDiagnostic(), new FullReferenceQualityAnalyzer(new ImagePairValidator()));
        using var session = await useCase.ExecuteAsync(baseline, plan, null, cancellation.Token);
        Assert.False(session.Report.IsComplete); Assert.Empty(session.Report.Cases); Assert.Empty(session.Report.Curves);
    }

    private static PixelImage Solid(int width, int height, byte value) { var bytes = new byte[width * height * 4]; for (var i = 0; i < bytes.Length; i += 4) { bytes[i] = bytes[i + 1] = bytes[i + 2] = value; bytes[i + 3] = 255; } return new(new(width, height), bytes); }
    private static PixelImage Textured(int width, int height) { var bytes = new byte[width * height * 4]; for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) { var o = (y * width + x) * 4; bytes[o] = (byte)((x * 11 + y * 3) & 255); bytes[o + 1] = (byte)((x * 5 + y * 13) & 255); bytes[o + 2] = (byte)((x * 17 + y * 7) & 255); bytes[o + 3] = 255; } return new(new(width, height), bytes); }
    private sealed class FixedRandom : IRandomSource { private byte _next = 1; public void Fill(Span<byte> destination) { for (var i = 0; i < destination.Length; i++) destination[i] = _next++; } }
    private sealed class ThrowCodec : IImageCodec
    {
        public Task<PixelImage> DecodeAsync(string path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PixelImage> DecodeAsync(ReadOnlyMemory<byte> encodedImage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<byte[]> EncodeAsync(PixelImage image, ImageOutputFormat format, int jpegQuality, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class NeverDiagnostic : IWatermarkDiagnosticReader
    { public WatermarkDiagnosticResult Read(PixelImage image, ControlledWatermarkBaseline baseline, ReadOnlySpan<byte> expectedPayload, string? password, CancellationToken cancellationToken) => throw new InvalidOperationException(); }
}
