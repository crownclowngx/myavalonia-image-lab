using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ImageLabPlugin.Constants;
using ImageLabPlugin.Features.WatermarkEmbed;
using ImageLabPlugin.Features.WatermarkInspect;
using ImageLabPlugin.Features.SpectrumInspector;
using ImageLabPlugin.Features.ImageCompareLab;
using ImageLabPlugin.Features.RobustnessLab;
using ImageLabPlugin.Features.ImageFingerprint;
using ImageLabPlugin.Features.BitPlaneViewer;
using ImageLabPlugin.Features.LsbSteganographyLab;
using ImageLabPlugin.Features.ConvolutionPlayground;
using ImageLabPlugin.Features.WaveletLab;
using ImageLabPlugin.Infrastructure.Persistence;
using ImageLabPlugin.Plugin;
using ImageLabPlugin.Domain.Frequency;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;
using Xunit;

namespace ImageLabPlugin.Tests;

/// <summary>覆盖插件贡献、Scoped Document 隔离和原子文件发布。</summary>
public sealed class CompositionAndPersistenceTests
{
    [Fact]
    public void Module只贡献十个稳定的PersistableDocument且不贡献Tool()
    {
        var registration = new RecordingRegistration();

        new ImageLabPluginModule().Configure(registration);

        Assert.Equal(
            new[] { PluginIds.WatermarkEmbedDocument, PluginIds.WatermarkInspectDocument, PluginIds.SpectrumInspectorDocument, PluginIds.ImageCompareLabDocument, PluginIds.RobustnessLabDocument, PluginIds.ImageFingerprintDocument, PluginIds.BitPlaneViewerDocument, PluginIds.LsbSteganographyLabDocument, PluginIds.ConvolutionPlaygroundDocument, PluginIds.WaveletLabDocument },
            registration.PersistableDocumentIds);
        Assert.Empty(registration.DocumentIds);
        Assert.Empty(registration.ToolIds);
    }

    [Fact]
    public void 两个Scope解析出的Document实例彼此隔离并共享无状态算法服务()
    {
        var registration = new RecordingRegistration();
        registration.Services.AddSingleton<IPluginWindowInteraction, NullWindowInteraction>();
        registration.Services.AddScoped<IDocumentLifetime, TestLifetime>();
        new ImageLabPluginModule().Configure(registration);
        using var provider = registration.Services.BuildServiceProvider(validateScopes: true);
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        var first = firstScope.ServiceProvider.GetRequiredService<WatermarkEmbedDocument>();
        var second = secondScope.ServiceProvider.GetRequiredService<WatermarkEmbedDocument>();
        var inspect = firstScope.ServiceProvider.GetRequiredService<WatermarkInspectDocument>();
        var spectrum = firstScope.ServiceProvider.GetRequiredService<SpectrumInspectorDocument>();
        var secondSpectrum = secondScope.ServiceProvider.GetRequiredService<SpectrumInspectorDocument>();
        var compare = firstScope.ServiceProvider.GetRequiredService<ImageCompareLabDocument>();
        var secondCompare = secondScope.ServiceProvider.GetRequiredService<ImageCompareLabDocument>();
        var robustness = firstScope.ServiceProvider.GetRequiredService<RobustnessLabDocument>();
        var secondRobustness = secondScope.ServiceProvider.GetRequiredService<RobustnessLabDocument>();
        var fingerprint = firstScope.ServiceProvider.GetRequiredService<ImageFingerprintDocument>();
        var secondFingerprint = secondScope.ServiceProvider.GetRequiredService<ImageFingerprintDocument>();
        var bitPlane = firstScope.ServiceProvider.GetRequiredService<BitPlaneViewerDocument>();
        var secondBitPlane = secondScope.ServiceProvider.GetRequiredService<BitPlaneViewerDocument>();
        var lsb = firstScope.ServiceProvider.GetRequiredService<LsbSteganographyLabDocument>();
        var secondLsb = secondScope.ServiceProvider.GetRequiredService<LsbSteganographyLabDocument>();
        var convolution = firstScope.ServiceProvider.GetRequiredService<ConvolutionPlaygroundDocument>();
        var secondConvolution = secondScope.ServiceProvider.GetRequiredService<ConvolutionPlaygroundDocument>();
        var wavelet = firstScope.ServiceProvider.GetRequiredService<WaveletLabDocument>();
        var secondWavelet = secondScope.ServiceProvider.GetRequiredService<WaveletLabDocument>();

        Assert.NotSame(first, second);
        Assert.NotSame(first, inspect);
        Assert.NotSame(first, spectrum);
        Assert.NotSame(spectrum, secondSpectrum);
        Assert.NotSame(compare, secondCompare);
        Assert.NotSame(robustness, secondRobustness);
        Assert.NotSame(fingerprint, secondFingerprint);
        Assert.NotSame(bitPlane, secondBitPlane);
        Assert.NotSame(lsb, secondLsb);
        Assert.NotSame(convolution, secondConvolution);
        Assert.NotSame(wavelet, secondWavelet);
        wavelet.SourcePath = "scope-wavelet-one";
        Assert.Empty(secondWavelet.SourcePath);
        convolution.SourcePath = "scope-convolution-one";
        convolution.KernelText = "0 0 0\n0 1 0\n0 0 0";
        Assert.Empty(secondConvolution.SourcePath);
        Assert.NotEqual(convolution.KernelText, secondConvolution.KernelText);
        lsb.SourcePath = "scope-lsb-one";
        lsb.SeedText = "42";
        Assert.Empty(secondLsb.SourcePath);
        Assert.Equal("1", secondLsb.SeedText);
        bitPlane.SourcePath = "scope-bit-plane-one";
        Assert.Empty(secondBitPlane.SourcePath);
        fingerprint.ReferencePath = "scope-fingerprint-one";
        Assert.Empty(secondFingerprint.ReferencePath);
        robustness.SourcePath = "scope-robustness-one";
        Assert.Empty(secondRobustness.SourcePath);
        compare.ReferencePath = "scope-compare-one";
        Assert.Empty(secondCompare.ReferencePath);
        spectrum.SourcePath = "scope-spectrum-one";
        Assert.Empty(secondSpectrum.SourcePath);
        Assert.Same(
            firstScope.ServiceProvider.GetRequiredService<Fft1DTransform>(),
            secondScope.ServiceProvider.GetRequiredService<Fft1DTransform>());
        first.PayloadText = "scope-one";
        Assert.Empty(second.PayloadText);
    }

    [Fact]
    public async Task 原子写入可替换目标且不遗留临时文件()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"image-lab-atomic-{Guid.NewGuid():N}");
        var target = Path.Combine(directory, "result.bin");
        Directory.CreateDirectory(directory);
        try
        {
            var writer = new AtomicFileWriter();
            await writer.WriteAsync(target, new byte[] { 1, 2, 3 }, CancellationToken.None);
            await writer.WriteAsync(target, new byte[] { 7, 8 }, CancellationToken.None);

            Assert.Equal(new byte[] { 7, 8 }, await File.ReadAllBytesAsync(target));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class RecordingRegistration : IPluginRegistration
    {
        public PluginId PluginId => PluginIds.Plugin;
        public IServiceCollection Services { get; } = new ServiceCollection();
        public List<DocumentTypeId> DocumentIds { get; } = [];
        public List<DocumentTypeId> PersistableDocumentIds { get; } = [];
        public List<ToolTypeId> ToolIds { get; } = [];

        public void UseLifecycle<TLifecycle>() where TLifecycle : class, IPluginLifecycle =>
            Services.AddSingleton<TLifecycle>();

        public void AddDocument<TDocument, TView>(DocumentDescriptor descriptor)
            where TDocument : class, IPluginDocument
            where TView : Control, new()
        {
            DocumentIds.Add(descriptor.DocumentTypeId);
            Services.AddScoped<TDocument>();
        }

        public void AddPersistableDocument<TDocument, TView>(DocumentDescriptor descriptor)
            where TDocument : class, IPersistablePluginDocument
            where TView : Control, new()
        {
            PersistableDocumentIds.Add(descriptor.DocumentTypeId);
            Services.AddScoped<TDocument>();
        }

        public void AddTool<TTool, TView>(ToolDescriptor descriptor)
            where TTool : class
            where TView : Control, new()
        {
            ToolIds.Add(descriptor.ToolTypeId);
        }
    }

    private sealed class NullWindowInteraction : IPluginWindowInteraction
    {
        public Task<IReadOnlyList<string>> PickOpenFilesAsync(FilePickerOpenOptions options, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<string?> PickSaveFileAsync(FilePickerSaveOptions options, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);

        public Task<bool> TrySetClipboardTextAsync(string text, CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }

    private sealed class TestLifetime : IDocumentLifetime
    {
        public CancellationToken ClosingToken => CancellationToken.None;
        public bool IsClosing => false;
    }
}
