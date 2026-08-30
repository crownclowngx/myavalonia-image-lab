using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ImageLabPlugin.Constants;
using ImageLabPlugin.Features.WatermarkEmbed;
using ImageLabPlugin.Features.WatermarkInspect;
using ImageLabPlugin.Features.SpectrumInspector;
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
    public void Module只贡献三个稳定的PersistableDocument且不贡献Tool()
    {
        var registration = new RecordingRegistration();

        new ImageLabPluginModule().Configure(registration);

        Assert.Equal(
            new[] { PluginIds.WatermarkEmbedDocument, PluginIds.WatermarkInspectDocument, PluginIds.SpectrumInspectorDocument },
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

        Assert.NotSame(first, second);
        Assert.NotSame(first, inspect);
        Assert.NotSame(first, spectrum);
        Assert.NotSame(spectrum, secondSpectrum);
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
