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
using ImageLabPlugin.Features.FrequencyFilter;
using ImageLabPlugin.Features.FrequencyMaskEditor;
using ImageLabPlugin.Features.PeriodicNoiseRemoval;
using ImageLabPlugin.Features.SvdDecomposition;
using ImageLabPlugin.Domain.FrequencyFiltering;
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
    public void Module只贡献十四个稳定的PersistableDocument且不贡献Tool()
    {
        var registration = new RecordingRegistration();

        new ImageLabPluginModule().Configure(registration);

        Assert.Equal(
            new[] { PluginIds.WatermarkEmbedDocument, PluginIds.WatermarkInspectDocument, PluginIds.SpectrumInspectorDocument, PluginIds.ImageCompareLabDocument, PluginIds.RobustnessLabDocument, PluginIds.ImageFingerprintDocument, PluginIds.BitPlaneViewerDocument, PluginIds.LsbSteganographyLabDocument, PluginIds.ConvolutionPlaygroundDocument, PluginIds.WaveletLabDocument, PluginIds.FrequencyFilterDocument, PluginIds.FrequencyMaskEditorDocument, PluginIds.PeriodicNoiseRemovalDocument, PluginIds.SvdDecompositionDocument },
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
        var frequencyFilter = firstScope.ServiceProvider.GetRequiredService<FrequencyFilterDocument>();
        var secondFrequencyFilter = secondScope.ServiceProvider.GetRequiredService<FrequencyFilterDocument>();
        var frequencyMask = firstScope.ServiceProvider.GetRequiredService<FrequencyMaskEditorDocument>();
        var secondFrequencyMask = secondScope.ServiceProvider.GetRequiredService<FrequencyMaskEditorDocument>();
        var periodicNoise = firstScope.ServiceProvider.GetRequiredService<PeriodicNoiseRemovalDocument>();
        var secondPeriodicNoise = secondScope.ServiceProvider.GetRequiredService<PeriodicNoiseRemovalDocument>();
        var svd = firstScope.ServiceProvider.GetRequiredService<SvdDecompositionDocument>();
        var secondSvd = secondScope.ServiceProvider.GetRequiredService<SvdDecompositionDocument>();

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
        Assert.NotSame(frequencyFilter, secondFrequencyFilter);
        Assert.NotSame(frequencyMask, secondFrequencyMask);
        Assert.NotSame(periodicNoise, secondPeriodicNoise);
        Assert.NotSame(svd, secondSvd);
        svd.SourcePath = "scope-svd-one";
        Assert.Empty(secondSvd.SourcePath);
        periodicNoise.SourcePath = "scope-periodic-one";
        Assert.Empty(secondPeriodicNoise.SourcePath);
        frequencyMask.SourcePath = "scope-frequency-mask-one";
        Assert.Empty(secondFrequencyMask.SourcePath);
        frequencyFilter.SourcePath = "scope-frequency-filter-one";
        Assert.Empty(secondFrequencyFilter.SourcePath);
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
        Assert.Same(
            firstScope.ServiceProvider.GetRequiredService<RadialFilterResponse>(),
            secondScope.ServiceProvider.GetRequiredService<RadialFilterResponse>());
        first.PayloadText = "scope-one";
        Assert.Empty(second.PayloadText);
    }

    [Fact]
    public async Task 频域滤波快照只恢复轻量参数且不自动建立Session()
    {
        var registration = new RecordingRegistration();
        registration.Services.AddSingleton<IPluginWindowInteraction, NullWindowInteraction>();
        registration.Services.AddScoped<IDocumentLifetime, TestLifetime>();
        new ImageLabPluginModule().Configure(registration);
        using var provider = registration.Services.BuildServiceProvider(validateScopes: true);
        using var firstScope = provider.CreateScope();
        var document = firstScope.ServiceProvider.GetRequiredService<FrequencyFilterDocument>();
        await document.InitializeAsync(new NewDocumentActivation("频域滤波"), default);
        document.SourcePath = "missing.png"; document.SelectedKind = "带阻"; document.SelectedFamily = "Butterworth";
        document.InnerCutoff = 0.22; document.OuterCutoff = 0.71; document.ButterworthOrder = 6; document.KernelSize = 15;
        var snapshot = await document.CaptureSaveSnapshotAsync(default);

        using var secondScope = provider.CreateScope();
        var restored = secondScope.ServiceProvider.GetRequiredService<FrequencyFilterDocument>();
        await restored.InitializeAsync(new RestoreDocumentActivation("频域滤波", snapshot.Content), default);
        Assert.Equal("带阻", restored.SelectedKind); Assert.Equal("Butterworth", restored.SelectedFamily);
        Assert.Equal(0.22, restored.InnerCutoff); Assert.Equal(0.71, restored.OuterCutoff); Assert.Equal(6, restored.ButterworthOrder);
        Assert.Equal(15, restored.KernelSize); Assert.False(restored.HasSession); Assert.False(restored.HasResult); Assert.False(restored.IsDirty);
    }

    [Fact]
    public async Task 频谱遮罩快照保存有界配方但不保存大数组且恢复不自动FFT()
    {
        var registration = new RecordingRegistration();
        registration.Services.AddSingleton<IPluginWindowInteraction, NullWindowInteraction>();
        registration.Services.AddScoped<IDocumentLifetime, TestLifetime>();
        new ImageLabPluginModule().Configure(registration);
        using var provider = registration.Services.BuildServiceProvider(validateScopes: true);
        using var firstScope = provider.CreateScope();
        var document = firstScope.ServiceProvider.GetRequiredService<FrequencyMaskEditorDocument>();
        await document.InitializeAsync(new NewDocumentActivation("遮罩编辑"), default);
        document.SourcePath = "missing.png";
        document.SelectedTool = "圆环";
        document.Strength = 0.65;
        document.InvertAllCommand.Execute(null);
        var snapshot = await document.CaptureSaveSnapshotAsync(default);
        var json = snapshot.Content.Payload.GetRawText();
        Assert.Contains("invertAll", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Complex", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rgba", json, StringComparison.OrdinalIgnoreCase);

        using var secondScope = provider.CreateScope();
        var restored = secondScope.ServiceProvider.GetRequiredService<FrequencyMaskEditorDocument>();
        await restored.InitializeAsync(new RestoreDocumentActivation("恢复遮罩", snapshot.Content), default);
        Assert.Equal("圆环", restored.SelectedTool);
        Assert.Equal(0.65, restored.Strength);
        Assert.True(restored.CanUndo);
        Assert.False(restored.HasSession);
        Assert.False(restored.HasResult);
        Assert.False(restored.IsDirty);
    }

    [Fact]
    public async Task Svd快照只保存轻量参数且恢复不自动读取或分解()
    {
        var registration = new RecordingRegistration();
        registration.Services.AddSingleton<IPluginWindowInteraction, NullWindowInteraction>();
        registration.Services.AddScoped<IDocumentLifetime, TestLifetime>();
        new ImageLabPluginModule().Configure(registration);
        using var provider = registration.Services.BuildServiceProvider(validateScopes: true);
        using var firstScope = provider.CreateScope();
        var document = firstScope.ServiceProvider.GetRequiredService<SvdDecompositionDocument>();
        await document.InitializeAsync(new NewDocumentActivation("奇异值分解重建"), default);
        document.SourcePath = "missing.png";
        document.AnalysisMaximumEdge = 256;
        document.SelectedStrategy = "YCbCr 独立";
        document.SelectedChannel = "Cr";
        document.RankMaximum = 10;
        document.ComponentMaximum = 9;
        document.Rank = 7;
        document.ComponentIndex = 3;
        var snapshot = await document.CaptureSaveSnapshotAsync(default);
        var json = snapshot.Content.Payload.GetRawText();
        Assert.Contains("one-sided-jacobi-v1", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"SingularValues\":", json, StringComparison.Ordinal);
        Assert.DoesNotContain("rgba", json, StringComparison.OrdinalIgnoreCase);

        using var secondScope = provider.CreateScope();
        var restored = secondScope.ServiceProvider.GetRequiredService<SvdDecompositionDocument>();
        await restored.InitializeAsync(new RestoreDocumentActivation("恢复 SVD", snapshot.Content), default);
        Assert.Equal(256, restored.AnalysisMaximumEdge);
        Assert.Equal("YCbCr 独立", restored.SelectedStrategy);
        Assert.Equal("Cr", restored.SelectedChannel);
        Assert.Equal(7, restored.Rank);
        Assert.Equal(3, restored.ComponentIndex);
        Assert.False(restored.HasSession);
        Assert.False(restored.HasDecomposition);
        Assert.False(restored.IsDirty);
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
