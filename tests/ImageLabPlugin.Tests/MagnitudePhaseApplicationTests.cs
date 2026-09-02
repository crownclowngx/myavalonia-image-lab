using System.Buffers.Binary;
using System.Text;
using ImageLabPlugin.Application.MagnitudePhaseSwap;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.Imaging;
using ImageLabPlugin.Domain.MagnitudePhaseSwap;
using ImageLabPlugin.Plugin;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class MagnitudePhaseApplicationTests
{
    [Fact]
    public async Task 准备阶段每输入只解码一次且Session持有共同频谱()
    {
        var codec = new TestCodec(Gradient(19, 13), Gradient(11, 17));
        using var provider = Provider(codec);
        using var session = await provider.GetRequiredService<IPrepareMagnitudePhasePairUseCase>()
            .ExecuteAsync(new PrepareMagnitudePhasePairRequest("a.png", "b.png", 256), default);
        Assert.Equal(1, codec.PathDecodeCounts["a.png"]); Assert.Equal(1, codec.PathDecodeCounts["b.png"]);
        Assert.Equal(256, session.CanvasA.Size); Assert.Equal(256 * 256, session.SpectrumA.ValueCount);
        Assert.NotEqual(session.FingerprintA, session.FingerprintB);
        Assert.Equal(new ImageSize(256, 256), session.MagnitudeA.Size);
    }

    [Fact]
    public async Task 交换结果原子提交且供体共轭虚部Parseval门禁通过()
    {
        using var provider = Provider(new TestCodec(Gradient(23, 17), Checker(17, 23)));
        using var session = await provider.GetRequiredService<IPrepareMagnitudePhasePairUseCase>()
            .ExecuteAsync(new PrepareMagnitudePhasePairRequest("a.png", "b.png", 256), default);
        var recipe = new MagnitudePhaseRecipe(256, MagnitudeComponentMode.SourceA, 0d,
            PhaseComponentMode.SourceB, 0d, MagnitudePhaseProjectionKind.PhysicalClamp);
        var generation = session.AdvanceGeneration();
        var result = await provider.GetRequiredService<IRenderMagnitudePhaseExperimentUseCase>()
            .ExecuteAsync(session, recipe, generation, default);
        Assert.True(session.TryCommit(result, generation, recipe.Fingerprint()));
        Assert.Same(result, session.CurrentResult);
        Assert.Equal(new ImageSize(256, 256), result.ResultImage.Size);
        Assert.InRange(result.Diagnostics.Mix.MaximumConjugateError, 0d, 1e-12);
        Assert.InRange(result.Diagnostics.MaximumImaginaryResidual, 0d, 1e-8);
        Assert.InRange(result.Diagnostics.Result.ParsevalRelativeError, 0d, 1e-10);
        Assert.Equal(MagnitudePhaseMetricStatus.Available, result.Diagnostics.Spatial.NccA.Status);
    }

    [Fact]
    public async Task 科学投影明确将Psnr与Ssim标记为不适用()
    {
        using var provider = Provider(new TestCodec(Gradient(16, 16), Checker(16, 16)));
        using var session = await provider.GetRequiredService<IPrepareMagnitudePhasePairUseCase>()
            .ExecuteAsync(new PrepareMagnitudePhasePairRequest("a.png", "b.png", 256), default);
        var recipe = new MagnitudePhaseRecipe(256, MagnitudeComponentMode.UnitNonZero, 0d,
            PhaseComponentMode.SourceA, 0d, MagnitudePhaseProjectionKind.SignedScientific);
        var generation = session.AdvanceGeneration();
        var result = await provider.GetRequiredService<IRenderMagnitudePhaseExperimentUseCase>()
            .ExecuteAsync(session, recipe, generation, default);
        Assert.Equal("诊断显示，不保留原亮度量纲", result.DiagnosticLabel);
        Assert.Equal(MagnitudePhaseMetricStatus.NotApplicable, result.Diagnostics.Spatial.PsnrA.Status);
        Assert.Equal(MagnitudePhaseMetricStatus.NotApplicable, result.Diagnostics.Spatial.SsimB.Status);
    }

    [Fact]
    public async Task 迟到候选被拒绝且最后有效结果保持不变()
    {
        using var provider = Provider(new TestCodec(Gradient(12, 12), Checker(12, 12)));
        using var session = await provider.GetRequiredService<IPrepareMagnitudePhasePairUseCase>()
            .ExecuteAsync(new PrepareMagnitudePhasePairRequest("a.png", "b.png", 256), default);
        var recipe = new MagnitudePhaseRecipe(256, MagnitudeComponentMode.SourceB, 0d,
            PhaseComponentMode.SourceA, 0d, MagnitudePhaseProjectionKind.PhysicalClamp);
        var generation = session.AdvanceGeneration();
        var candidate = await provider.GetRequiredService<IRenderMagnitudePhaseExperimentUseCase>()
            .ExecuteAsync(session, recipe, generation, default);
        Assert.True(session.TryCommit(candidate, generation, recipe.Fingerprint()));
        session.AdvanceGeneration();
        Assert.False(session.TryCommit(candidate, generation, recipe.Fingerprint()));
        Assert.Same(candidate, session.CurrentResult);
    }

    [Fact]
    public async Task 预取消和关闭都会阻止继续使用Session()
    {
        using var provider = Provider(new TestCodec(Gradient(12, 12), Checker(12, 12)));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetRequiredService<IPrepareMagnitudePhasePairUseCase>()
            .ExecuteAsync(new PrepareMagnitudePhasePairRequest("a.png", "b.png", 256), cancellation.Token));
        var session = await provider.GetRequiredService<IPrepareMagnitudePhasePairUseCase>()
            .ExecuteAsync(new PrepareMagnitudePhasePairRequest("a.png", "b.png", 256), default);
        session.Dispose();
        Assert.Throws<ObjectDisposedException>(() => session.AdvanceGeneration());
    }

    [Fact]
    public async Task 同频点探针即时组合而不缓存第三份结果频谱()
    {
        using var provider = Provider(new TestCodec(Gradient(12, 12), Checker(12, 12)));
        using var session = await provider.GetRequiredService<IPrepareMagnitudePhasePairUseCase>()
            .ExecuteAsync(new PrepareMagnitudePhasePairRequest("a.png", "b.png", 256), default);
        var recipe = new MagnitudePhaseRecipe(256, MagnitudeComponentMode.SourceA, 0d,
            PhaseComponentMode.SourceB, 0d, MagnitudePhaseProjectionKind.PhysicalClamp);
        var point = provider.GetRequiredService<IInspectMagnitudePhasePointUseCase>().Execute(session, recipe, 128, 128);
        Assert.Equal(0, point.CenteredKx); Assert.Equal(0, point.CenteredKy); Assert.True(point.IsSelfConjugate);
        Assert.Equal(point.MagnitudeA, point.ResultMagnitude, 8);
    }

    [Fact]
    public void 严格配方可往返并拒绝未知重复与指纹篡改()
    {
        using var provider = Provider(new TestCodec(Gradient(8, 8), Checker(8, 8)));
        var serializer = provider.GetRequiredService<IMagnitudePhaseRecipeSerializer>();
        var recipe = new MagnitudePhaseRecipe(512, MagnitudeComponentMode.LinearAtoB, .25d,
            PhaseComponentMode.SourceB, 0d, MagnitudePhaseProjectionKind.PhysicalClamp);
        var bytes = serializer.Serialize(recipe, new string('a', 24), new string('b', 24));
        var restored = serializer.Deserialize(bytes, out var a, out var b);
        Assert.Equal(recipe.Fingerprint(), restored.Fingerprint()); Assert.Equal(new string('a', 24), a); Assert.Equal(new string('b', 24), b);
        var text = Encoding.UTF8.GetString(bytes);
        Assert.Throws<InvalidDataException>(() => serializer.Deserialize(Encoding.UTF8.GetBytes(
            text.Replace("\"schema\": 1,", "\"schema\": 1,\n  \"unknown\": true,", StringComparison.Ordinal)), out _, out _));
        Assert.Throws<InvalidDataException>(() => serializer.Deserialize(Encoding.UTF8.GetBytes(
            text.Replace("\"schema\": 1,", "\"schema\": 1,\n  \"schema\": 1,", StringComparison.Ordinal)), out _, out _));
        Assert.Throws<InvalidDataException>(() => serializer.Deserialize(Encoding.UTF8.GetBytes(
            text.Replace("\"magnitudeAmount\": 0.25", "\"magnitudeAmount\": 0.5", StringComparison.Ordinal)), out _, out _));
    }

    [Fact]
    public async Task 导出只允许当前结果并执行内存和真实目标回读()
    {
        var codec = new TestCodec(Gradient(10, 10), Checker(10, 10));
        using var provider = Provider(codec);
        using var session = await provider.GetRequiredService<IPrepareMagnitudePhasePairUseCase>()
            .ExecuteAsync(new PrepareMagnitudePhasePairRequest("a.png", "b.png", 256), default);
        var recipe = new MagnitudePhaseRecipe(256, MagnitudeComponentMode.SourceA, 0d,
            PhaseComponentMode.Zero, 0d, MagnitudePhaseProjectionKind.PhysicalClamp);
        var generation = session.AdvanceGeneration();
        var result = await provider.GetRequiredService<IRenderMagnitudePhaseExperimentUseCase>()
            .ExecuteAsync(session, recipe, generation, default);
        var exporter = provider.GetRequiredService<IExportMagnitudePhaseImageUseCase>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExecuteAsync(session, result,
            Path.Combine(Path.GetTempPath(), $"uncommitted-{Guid.NewGuid():N}.png"), default));
        Assert.True(session.TryCommit(result, generation, recipe.Fingerprint()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExecuteAsync(session, result, "a.png", default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetRequiredService<IExportMagnitudePhaseRecipeUseCase>()
            .ExecuteAsync(recipe, session, "b.png", default));
        var path = Path.Combine(Path.GetTempPath(), $"magnitude-phase-{Guid.NewGuid():N}.png");
        try
        {
            await exporter.ExecuteAsync(session, result, path, default);
            Assert.True(File.Exists(path));
            Assert.Equal(result.ResultImage.Rgba.ToArray(), (await codec.DecodeAsync(path, default)).Rgba.ToArray());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void 报告Json和Csv不泄漏路径且跨文化稳定()
    {
        using var provider = Provider(new TestCodec(Gradient(8, 8), Checker(8, 8)));
        var serializer = provider.GetRequiredService<IMagnitudePhaseReportSerializer>();
        var recipe = new MagnitudePhaseRecipe(256, MagnitudeComponentMode.SourceA, 0d,
            PhaseComponentMode.SourceB, 0d, MagnitudePhaseProjectionKind.PhysicalClamp);
        var metric = MagnitudePhaseMetric.Available(.125d, "ratio");
        var spatial = new MagnitudePhaseSpatialDiagnostics(metric, metric, metric, metric, metric, metric, metric, metric);
        var energy = new MagnitudePhaseEnergyDiagnostics(1, 2, 3, .25);
        var diagnostics = new MagnitudePhaseDiagnosticsResult(new SpectrumMixDiagnostics(0, 0, 0, 0, 0, 0, 0),
            spatial, energy, energy, energy, new MagnitudePhaseProjectionStatistics(0, 255, 128, 0, 0, 0), 0, 0);
        var report = new MagnitudePhaseReport(MagnitudePhaseProtocol.Report, 1, new string('a', 24), new string('b', 24),
            recipe.Fingerprint(), recipe, diagnostics, 12, "1.0.0", "不含路径");
        var json = Encoding.UTF8.GetString(serializer.SerializeJson(report));
        var csv = Encoding.UTF8.GetString(serializer.SerializeCsv(report));
        Assert.DoesNotContain("C:\\", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0.125", csv, StringComparison.Ordinal); Assert.DoesNotContain("0,125", csv, StringComparison.Ordinal);
    }

    private static ServiceProvider Provider(TestCodec codec)
    {
        var services = new ServiceCollection(); services.AddImageLabPluginServices(); services.AddSingleton<IImageCodec>(codec);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static PixelImage Gradient(int width, int height)
    {
        var bytes = new byte[width * height * 4];
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        { var i = ((y * width) + x) * 4; bytes[i] = (byte)((x * 13 + y * 3) % 256); bytes[i + 1] = (byte)((x * 5 + y * 11) % 256); bytes[i + 2] = (byte)((x + y * 7) % 256); bytes[i + 3] = 255; }
        return new PixelImage(new ImageSize(width, height), bytes);
    }

    private static PixelImage Checker(int width, int height)
    {
        var bytes = new byte[width * height * 4];
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        { var level = ((x / 3 + y / 3) & 1) == 0 ? (byte)32 : (byte)224; var i = ((y * width) + x) * 4; bytes[i] = bytes[i + 1] = bytes[i + 2] = level; bytes[i + 3] = 255; }
        return new PixelImage(new ImageSize(width, height), bytes);
    }

    private sealed class TestCodec(PixelImage a, PixelImage b) : IImageCodec
    {
        public Dictionary<string, int> PathDecodeCounts { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Task<PixelImage> DecodeAsync(string path, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); PathDecodeCounts[path] = PathDecodeCounts.GetValueOrDefault(path) + 1;
            if (File.Exists(path)) return DecodeAsync(File.ReadAllBytes(path), cancellationToken);
            return Task.FromResult((path.Contains('a', StringComparison.OrdinalIgnoreCase) ? a : b).Clone());
        }
        public Task<PixelImage> DecodeAsync(ReadOnlyMemory<byte> encodedImage, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); var span = encodedImage.Span;
            var width = BinaryPrimitives.ReadInt32LittleEndian(span); var height = BinaryPrimitives.ReadInt32LittleEndian(span[4..]);
            return Task.FromResult(new PixelImage(new ImageSize(width, height), span[8..]));
        }
        public Task<byte[]> EncodeAsync(PixelImage image, ImageOutputFormat format, int jpegQuality, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Assert.Equal(ImageOutputFormat.Png, format);
            var bytes = new byte[8 + image.Rgba.Length]; BinaryPrimitives.WriteInt32LittleEndian(bytes, image.Size.Width);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), image.Size.Height); image.Rgba.Span.CopyTo(bytes.AsSpan(8));
            return Task.FromResult(bytes);
        }
    }
}
