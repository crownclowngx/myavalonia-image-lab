using Avalonia.Platform.Storage;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Application.Wavelets;
using ImageLabPlugin.Domain.Shared.Spectral;
using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Domain.Watermarking;
using ImageLabPlugin.Domain.Wavelets;
using ImageLabPlugin.Features.WaveletLab;
using ImageLabPlugin.Infrastructure.ErrorCorrection;
using ImageLabPlugin.Infrastructure.Watermarking;
using ImageLabPlugin.Infrastructure.Wavelets;
using ImageLabPlugin.Plugin;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class WaveletDocumentAndAdapterTests
{
    [Fact]
    public async Task 快照只保存路径与轻量参数且恢复不自动读取图片()
    {
        using var provider = CreateProvider(); using var scope = provider.CreateScope();
        var document = scope.ServiceProvider.GetRequiredService<WaveletLabDocument>();
        await document.InitializeAsync(new NewDocumentActivation("小波"), CancellationToken.None);
        document.SourcePath = "D:/missing/source.png"; document.ReferencePath = "D:/missing/reference.png";
        document.SelectedTransform = nameof(WaveletTransformId.Cdf53); document.SelectedChannel = nameof(ImageChannel.ChromaRed);
        document.Levels = 4; document.Threshold = 7.5d; document.AnalysisMaximumEdge = 512;
        var snapshot = await document.CaptureSaveSnapshotAsync(CancellationToken.None);
        var json = snapshot.Content.Payload.GetRawText();
        Assert.Contains("Cdf53", json, StringComparison.Ordinal); Assert.Contains("7.5", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Rgba", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PayloadText", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bitmap", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Coefficient", json, StringComparison.OrdinalIgnoreCase);

        using var secondScope = provider.CreateScope(); var restored = secondScope.ServiceProvider.GetRequiredService<WaveletLabDocument>();
        await restored.InitializeAsync(new RestoreDocumentActivation("恢复", snapshot.Content), CancellationToken.None);
        Assert.Equal(nameof(WaveletTransformId.Cdf53), restored.SelectedTransform);
        Assert.Equal(nameof(ImageChannel.ChromaRed), restored.SelectedChannel); Assert.Equal(4, restored.Levels);
        Assert.False(restored.HasSession); Assert.Contains("尚未自动", restored.StatusMessage, StringComparison.Ordinal);
        document.Dispose(); restored.Dispose();
    }

    [Fact]
    public async Task 未知Schema安全回退且不触发IO()
    {
        using var provider = CreateProvider(); using var scope = provider.CreateScope();
        var document = scope.ServiceProvider.GetRequiredService<WaveletLabDocument>();
        await document.InitializeAsync(new RestoreDocumentActivation("未知", new DocumentContent(99,
            System.Text.Json.JsonSerializer.SerializeToElement(new { coefficients = new[] { 1, 2 } }))), CancellationToken.None);
        Assert.Equal(nameof(WaveletTransformId.Haar), document.SelectedTransform);
        Assert.False(document.HasSession); Assert.Contains("不支持快照 schema", document.StatusMessage, StringComparison.Ordinal);
        document.Dispose();
    }

    [Fact]
    public async Task Dct与Dwt适配器均在自身协议下无扰动回读共同Payload()
    {
        var image = CreateOpaqueNoise(256, 256); var payload = "fair"u8.ToArray();
        var rs = new ReedSolomonCodec(); var frame = new WatermarkFrameProtocol(rs, new FixedRandomSource());
        var dct = new DctWatermarkBenchmarkAdapter(new FrequencyWatermarkCarrier(new Dct8x8Transform(), frame, rs), frame);
        var dwt = new DwtWatermarkBenchmarkAdapter(new DwtWatermarkCarrier(new HaarWaveletTransform(), new ImageChannelConverter()));
        foreach (var adapter in new IWatermarkBenchmarkCarrier[] { dct, dwt })
        {
            Assert.True(adapter.Estimate(image, payload.Length).MaximumPayloadBytes >= payload.Length);
            var result = await adapter.EmbedAndReadAsync(image, payload, CancellationToken.None);
            Assert.True(result.IntegrityValid); Assert.Equal(payload, result.RecoveredPayload); Assert.Equal(image.Size, result.Image.Size);
            Assert.Equal(0d, result.RawBitErrorRate);
        }
    }

    [Fact]
    public void Wavelet依赖方向和朴素模式由源码门禁固定()
    {
        var root = FindRepositoryRoot();
        var domain = ReadAll(Path.Combine(root, "src", "ImageLabPlugin.Plugin", "Domain", "Wavelets"));
        Assert.DoesNotContain("using Avalonia", domain, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Text.Json", domain, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.Extensions", domain, StringComparison.Ordinal);
        Assert.DoesNotContain("ImageLabPlugin.Application", domain, StringComparison.Ordinal);
        Assert.DoesNotContain("ImageLabPlugin.Infrastructure", domain, StringComparison.Ordinal);

        var application = ReadAll(Path.Combine(root, "src", "ImageLabPlugin.Plugin", "Application", "Wavelets"));
        Assert.DoesNotContain("using Avalonia", application, StringComparison.Ordinal);
        Assert.DoesNotContain("ImageLabPlugin.Infrastructure", application, StringComparison.Ordinal);
        var feature = ReadAll(Path.Combine(root, "src", "ImageLabPlugin.Plugin", "Features", "WaveletLab"));
        Assert.DoesNotContain("Inverse1D", feature, StringComparison.Ordinal);
        Assert.DoesNotContain("QuantizeDifference", feature, StringComparison.Ordinal);
        Assert.DoesNotContain("class WaveletService", domain + application + feature, StringComparison.Ordinal);
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection(); services.AddImageLabPluginServices();
        services.AddSingleton<IPluginWindowInteraction, NullWindowInteraction>();
        services.AddScoped<IDocumentLifetime, TestLifetime>(); services.AddScoped<WaveletLabDocument>();
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ImageLabPlugin.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("未找到 ImageLabPlugin.slnx。");
    }

    private static string ReadAll(string directory) => string.Join('\n',
        Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(File.ReadAllText));

    private static PixelImage CreateOpaqueNoise(int width, int height)
    {
        var random = new Random(99); var rgba = new byte[width * height * 4]; random.NextBytes(rgba);
        for (var i = 3; i < rgba.Length; i += 4) rgba[i] = 255;
        return new(new(width, height), rgba);
    }

    private sealed class FixedRandomSource : IRandomSource
    {
        private byte _next = 1;
        public void Fill(Span<byte> destination) { foreach (ref var value in destination) value = _next++; }
    }
    private sealed class NullWindowInteraction : IPluginWindowInteraction
    {
        public Task<IReadOnlyList<string>> PickOpenFilesAsync(FilePickerOpenOptions options, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> PickSaveFileAsync(FilePickerSaveOptions options, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task<bool> TrySetClipboardTextAsync(string text, CancellationToken cancellationToken) => Task.FromResult(false);
    }
    private sealed class TestLifetime : IDocumentLifetime
    {
        public CancellationToken ClosingToken => CancellationToken.None;
        public bool IsClosing => false;
    }
}
