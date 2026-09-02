using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ImageLabPlugin.Application.ImageOscilloscope;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Constants;
using ImageLabPlugin.Domain.ImageOscilloscope;
using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Features.ImageOscilloscope;
using ImageLabPlugin.Plugin;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class ImageOscilloscopeSessionDocumentViewTests
{
    [Fact]
    public async Task 应用用例只解码一次且Session独占完整分析与代理()
    {
        var codec = new TestCodec(Gradient(7, 5));
        using var provider = UseCaseProvider(codec);
        using var session = await provider.GetRequiredService<IPrepareImageOscilloscopeSessionUseCase>()
            .ExecuteAsync("sample.png", ClippingThresholds.Default, default);
        Assert.Equal(1, codec.DecodeCount);
        Assert.Equal(new ImageSize(7, 5), session.Analysis.SourceSize);
        Assert.Equal(new ImageSize(7, 5), session.Preview.Size);
        Assert.Equal(35UL, session.Analysis.RedHistogram.Aggregate(0UL, (sum, value) => sum + value));
        Assert.NotEmpty(session.SourceFingerprint);
    }

    [Fact]
    public async Task 裁切代次拒绝迟到候选且不改变主分析对象()
    {
        using var provider = UseCaseProvider(new TestCodec(Gradient(8, 6)));
        using var session = await provider.GetRequiredService<IPrepareImageOscilloscopeSessionUseCase>()
            .ExecuteAsync("sample.png", ClippingThresholds.Default, default);
        var analysis = session.Analysis;
        var useCase = provider.GetRequiredService<IRecalculateImageOscilloscopeClippingUseCase>();
        var firstGeneration = session.AdvanceClippingGeneration();
        var first = await useCase.ExecuteAsync(session, new ClippingThresholds(1, 254), firstGeneration, default);
        session.AdvanceClippingGeneration();
        Assert.False(session.TryCommitClipping(first, firstGeneration, session.SourceFingerprint));
        Assert.Same(analysis, session.Analysis);
        Assert.Equal(ClippingThresholds.Default, session.CurrentClipping.Thresholds);
    }

    [Fact]
    public async Task 显示模式只重投影已有计数且探针为常量时间读取()
    {
        using var provider = UseCaseProvider(new TestCodec(Gradient(8, 6)));
        using var session = await provider.GetRequiredService<IPrepareImageOscilloscopeSessionUseCase>()
            .ExecuteAsync("sample.png", ClippingThresholds.Default, default);
        var display = provider.GetRequiredService<IProjectImageOscilloscopeDisplayUseCase>();
        var logarithmic = display.Project(session, ScopeDensityMode.Logarithmic);
        var linear = display.Project(session, ScopeDensityMode.Linear);
        Assert.Same(session.Analysis, session.Analysis);
        Assert.Equal(logarithmic.Waveform.UpperCount, linear.Waveform.UpperCount);
        var probe = provider.GetRequiredService<IInspectImageOscilloscopePixelUseCase>().Execute(session, 7, 5);
        Assert.Equal((7, 5), (probe.SourceX, probe.SourceY));
    }

    [Fact]
    public async Task 快照不保存路径源像素和大数组且恢复不自动分析()
    {
        using var provider = DocumentProvider();
        using var firstScope = provider.CreateScope();
        var document = firstScope.ServiceProvider.GetRequiredService<ImageOscilloscopeDocument>();
        await document.InitializeAsync(new NewDocumentActivation("图像示波器"), default);
        document.SourcePath = @"C:\private\portrait.png";
        document.SelectedDensityMode = "线性";
        document.SelectedClippingMode = "RGB 任一通道";
        document.ShadowThreshold = 3; document.HighlightThreshold = 252;
        var snapshot = await document.CaptureSaveSnapshotAsync(default);
        var json = snapshot.Content.Payload.GetRawText();
        Assert.DoesNotContain("portrait", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rgba", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RedHistogram", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Counts", json, StringComparison.OrdinalIgnoreCase);

        using var secondScope = provider.CreateScope();
        var restored = secondScope.ServiceProvider.GetRequiredService<ImageOscilloscopeDocument>();
        await restored.InitializeAsync(new RestoreDocumentActivation("恢复示波器", snapshot.Content), default);
        Assert.Empty(restored.SourcePath); Assert.False(restored.HasAnalysis); Assert.False(restored.IsBusy);
        Assert.Equal("线性", restored.SelectedDensityMode); Assert.Equal(3, restored.ShadowThreshold); Assert.Equal(252, restored.HighlightThreshold);
        Assert.Contains("重新选择", restored.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void View与专用绘制控件具有可构造类型且绑定均有类型上下文()
    {
        Assert.True(typeof(ImageOscilloscopeView).IsSealed);
        Assert.True(typeof(ScopePlotControl).IsSealed);
        Assert.True(typeof(ScopeHistogramControl).IsSealed);
        var xaml = File.ReadAllText(Path.Combine(Root(), "src", "ImageLabPlugin.Plugin", "Features", "ImageOscilloscope", "ImageOscilloscopeView.axaml"));
        Assert.Contains("x:DataType=\"vm:ImageOscilloscopeDocument\"", xaml, StringComparison.Ordinal);
        Assert.Contains("PointerMoved=", xaml, StringComparison.Ordinal);
        Assert.Contains("RGB Parade", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Module在旧二十项后追加稳定Document且两个Scope互相隔离()
    {
        var registration = new RecordingRegistration();
        registration.Services.AddSingleton<IPluginWindowInteraction, NullWindowInteraction>();
        registration.Services.AddScoped<IDocumentLifetime, Lifetime>();
        new ImageLabPluginModule().Configure(registration);
        Assert.Equal(21, registration.Ids.Count);
        Assert.Equal(PluginIds.MagnitudePhaseSwapDocument, registration.Ids[^2]);
        Assert.Equal(PluginIds.ImageOscilloscopeDocument, registration.Ids[^1]);
        Assert.Empty(registration.ToolIds);
        using var provider = registration.Services.BuildServiceProvider(validateScopes: true);
        using var first = provider.CreateScope(); using var second = provider.CreateScope();
        var a = first.ServiceProvider.GetRequiredService<ImageOscilloscopeDocument>();
        var b = second.ServiceProvider.GetRequiredService<ImageOscilloscopeDocument>();
        Assert.NotSame(a, b);
        a.SourcePath = "a.png"; Assert.Empty(b.SourcePath);
        Assert.Same(first.ServiceProvider.GetRequiredService<ImageOscilloscopeAnalyzer>(),
            second.ServiceProvider.GetRequiredService<ImageOscilloscopeAnalyzer>());
    }

    [Fact]
    public void Solid依赖方向朴素模式中文注释和Nuget门禁成立()
    {
        var root = Root();
        var domain = ReadAll(Path.Combine(root, "src", "ImageLabPlugin.Plugin", "Domain", "ImageOscilloscope"));
        Assert.DoesNotContain("using Avalonia", domain, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Text.Json", domain, StringComparison.Ordinal);
        Assert.DoesNotContain("IServiceProvider", domain, StringComparison.Ordinal);
        Assert.DoesNotContain("Features.", domain, StringComparison.Ordinal);
        Assert.DoesNotContain("Mediator", domain, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Factory", domain, StringComparison.OrdinalIgnoreCase);
        var document = File.ReadAllText(Path.Combine(root, "src", "ImageLabPlugin.Plugin", "Features", "ImageOscilloscope", "ImageOscilloscopeDocument.cs"));
        Assert.DoesNotContain("0.299", document, StringComparison.Ordinal);
        Assert.DoesNotContain("PercentileUpper", document, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("for (", document, StringComparison.Ordinal);
        var production = ReadAll(Path.Combine(root, "src", "ImageLabPlugin.Plugin"));
        Assert.DoesNotContain("AIFLOW", production, StringComparison.OrdinalIgnoreCase);
        foreach (var relative in new[]
        {
            "Domain/ImageOscilloscope/OscilloscopeColorConverter.cs",
            "Domain/ImageOscilloscope/ImageOscilloscopeAnalyzer.cs",
            "Domain/ImageOscilloscope/ClippingAnalyzer.cs",
            "Domain/ImageOscilloscope/ScopeDensityProjector.cs",
            "Application/ImageOscilloscope/ImageOscilloscopeSession.cs",
            "Features/ImageOscilloscope/ImageOscilloscopeDocument.cs"
        })
        {
            var source = File.ReadAllText(Path.Combine(root, "src", "ImageLabPlugin.Plugin", relative.Replace('/', Path.DirectorySeparatorChar)));
            Assert.Contains("/// <summary>", source, StringComparison.Ordinal); Assert.Matches("[\u4e00-\u9fff]", source);
        }
        var project = XDocument.Load(Path.Combine(root, "src", "ImageLabPlugin.Plugin", "ImageLabPlugin.Plugin.csproj"));
        Assert.Equal(new[] { "CommunityToolkit.Mvvm", "MyAvaloniaManagement.Plugin.Build", "MyAvaloniaManagement.PluginSdk", "MyAvaloniaManagement.PluginSdk.UI" },
            project.Descendants("PackageReference").Select(node => (string?)node.Attribute("Include")).ToArray());
    }

    private static ServiceProvider UseCaseProvider(TestCodec codec)
    {
        var services = new ServiceCollection(); services.AddImageLabPluginServices(); services.AddSingleton<IImageCodec>(codec);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static ServiceProvider DocumentProvider()
    {
        var registration = new RecordingRegistration();
        registration.Services.AddSingleton<IPluginWindowInteraction, NullWindowInteraction>();
        registration.Services.AddScoped<IDocumentLifetime, Lifetime>();
        new ImageLabPluginModule().Configure(registration);
        return registration.Services.BuildServiceProvider(validateScopes: true);
    }

    private static PixelImage Gradient(int width, int height)
    {
        var bytes = new byte[width * height * 4];
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        { var offset = ((y * width) + x) * 4; bytes[offset] = (byte)(x * 17); bytes[offset + 1] = (byte)(y * 29); bytes[offset + 2] = (byte)((x + y) * 11); bytes[offset + 3] = 255; }
        return new PixelImage(new ImageSize(width, height), bytes);
    }

    private static string ReadAll(string directory) => string.Join('\n', Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(File.ReadAllText));
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null) { if (File.Exists(Path.Combine(directory.FullName, "ImageLabPlugin.slnx"))) return directory.FullName; directory = directory.Parent; }
        throw new DirectoryNotFoundException("未找到 ImageLabPlugin.slnx。");
    }

    private sealed class TestCodec(PixelImage source) : IImageCodec
    {
        public int DecodeCount { get; private set; }
        public Task<PixelImage> DecodeAsync(string path, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); DecodeCount++; return Task.FromResult(source.Clone()); }
        public Task<PixelImage> DecodeAsync(ReadOnlyMemory<byte> encodedImage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<byte[]> EncodeAsync(PixelImage image, ImageOutputFormat format, int jpegQuality, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingRegistration : IPluginRegistration
    {
        public PluginId PluginId => PluginIds.Plugin;
        public IServiceCollection Services { get; } = new ServiceCollection();
        public List<DocumentTypeId> Ids { get; } = [];
        public List<ToolTypeId> ToolIds { get; } = [];
        public void UseLifecycle<TLifecycle>() where TLifecycle : class, IPluginLifecycle => Services.AddSingleton<TLifecycle>();
        public void AddDocument<TDocument, TView>(DocumentDescriptor descriptor) where TDocument : class, IPluginDocument where TView : Control, new() => Services.AddScoped<TDocument>();
        public void AddPersistableDocument<TDocument, TView>(DocumentDescriptor descriptor) where TDocument : class, IPersistablePluginDocument where TView : Control, new()
        { Ids.Add(descriptor.DocumentTypeId); Services.AddScoped<TDocument>(); Services.AddTransient<TView>(); }
        public void AddTool<TTool, TView>(ToolDescriptor descriptor) where TTool : class where TView : Control, new() => ToolIds.Add(descriptor.ToolTypeId);
    }

    private sealed class NullWindowInteraction : IPluginWindowInteraction
    {
        public Task<IReadOnlyList<string>> PickOpenFilesAsync(FilePickerOpenOptions options, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> PickSaveFileAsync(FilePickerSaveOptions options, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task<bool> TrySetClipboardTextAsync(string text, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class Lifetime : IDocumentLifetime { public CancellationToken ClosingToken => CancellationToken.None; public bool IsClosing => false; }
}
