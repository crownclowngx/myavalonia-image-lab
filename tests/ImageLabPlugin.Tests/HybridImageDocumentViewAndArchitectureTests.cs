using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ImageLabPlugin.Application.HybridImage;
using ImageLabPlugin.Constants;
using ImageLabPlugin.Features.HybridImage;
using ImageLabPlugin.Plugin;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;
using Xunit;

namespace ImageLabPlugin.Tests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class HybridImageDocumentViewAndArchitectureTests
{
    [Fact]
    public void View类型可解析且坐标映射排除Letterbox()
    {
        Assert.True(typeof(HybridImageView).IsSealed);
        var point = HybridImageCoordinateMapper.ToNormalized(100, 50, 200, 100, 100, 100);
        Assert.Equal(.5d, point.X, 12);
        Assert.Equal(.5d, point.Y, 12);
        var leftLetterbox = HybridImageCoordinateMapper.ToNormalized(25, 50, 200, 100, 100, 100);
        Assert.Equal(0d, leftLetterbox.X);
    }

    [Fact]
    public async Task 快照脱敏且恢复不自动读取对齐或滤波()
    {
        var provider = CreateProvider();
        using var firstScope = provider.CreateScope();
        var document = firstScope.ServiceProvider.GetRequiredService<HybridImageDocument>();
        await document.InitializeAsync(new NewDocumentActivation("混合图像"), default);
        document.PathA = @"C:\private\face-a.png";
        document.PathB = @"D:\secret\face-b.png";
        document.LowSigmaPixels = 5.5;
        document.HighSigmaPixels = 4.25;
        var snapshot = await document.CaptureSaveSnapshotAsync(default);
        var json = snapshot.Content.Payload.GetRawText();
        Assert.Contains("face-a.png", json, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\private", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rgba", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("spectrum", json, StringComparison.OrdinalIgnoreCase);

        using var secondScope = provider.CreateScope();
        var restored = secondScope.ServiceProvider.GetRequiredService<HybridImageDocument>();
        await restored.InitializeAsync(new RestoreDocumentActivation("恢复混合图像", snapshot.Content), default);
        Assert.Empty(restored.PathA);
        Assert.Empty(restored.PathB);
        Assert.Equal(5.5, restored.LowSigmaPixels);
        Assert.Equal(4.25, restored.HighSigmaPixels);
        Assert.False(restored.HasInputs);
        Assert.False(restored.HasAlignment);
        Assert.False(restored.HasResult);
        Assert.False(restored.IsDirty);
    }

    [Fact]
    public void 领域依赖方向和朴素设计门禁成立()
    {
        var root = Root();
        var domain = ReadAll(Path.Combine(root, "src", "ImageLabPlugin.Plugin", "Domain", "HybridImage"));
        Assert.DoesNotContain("using Avalonia", domain, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Text.Json", domain, StringComparison.Ordinal);
        Assert.DoesNotContain("IServiceProvider", domain, StringComparison.Ordinal);
        Assert.DoesNotContain("Features.", domain, StringComparison.Ordinal);
        Assert.DoesNotContain("abstract factory", domain, StringComparison.OrdinalIgnoreCase);
        var document = File.ReadAllText(Path.Combine(root, "src", "ImageLabPlugin.Plugin", "Features", "HybridImage", "HybridImageDocument.cs"));
        Assert.DoesNotContain("Fft2DTransform", document, StringComparison.Ordinal);
        Assert.DoesNotContain("AlignedImageSampler", document, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonSerializer", document, StringComparison.Ordinal);
        Assert.DoesNotContain("for (", document, StringComparison.Ordinal);
        var contracts = File.ReadAllText(Path.Combine(root, "src", "ImageLabPlugin.Plugin", "Application", "HybridImage", "HybridImageContracts.cs"));
        Assert.DoesNotContain("using System.Numerics", contracts, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadOnlyMemory<Complex>", contracts, StringComparison.Ordinal);
    }

    [Fact]
    public void 核心生产文件有中文设计注释且Nuget白名单不变()
    {
        var root = Root();
        var files = new[]
        {
            "Domain/HybridImage/HybridAlignment.cs", "Domain/HybridImage/SimilarityTransformSolver.cs",
            "Domain/HybridImage/AlignedImageSampler.cs", "Domain/HybridImage/GaussianPlaneFilter.cs",
            "Domain/HybridImage/HybridImageComposer.cs", "Domain/HybridImage/HybridScaleProjector.cs",
            "Domain/HybridImage/HybridImageDiagnostics.cs", "Application/HybridImage/HybridImageContracts.cs",
            "Application/HybridImage/HybridImageSession.cs", "Application/HybridImage/HybridImageUseCases.cs",
            "Infrastructure/Persistence/HybridImageSerializers.cs", "Features/HybridImage/HybridImageDocument.cs"
        };
        foreach (var relative in files)
        {
            var source = File.ReadAllText(Path.Combine(root, "src", "ImageLabPlugin.Plugin", relative.Replace('/', Path.DirectorySeparatorChar)));
            Assert.Contains("/// <summary>", source, StringComparison.Ordinal);
            Assert.Matches("[\u4e00-\u9fff]", source);
        }
        var project = XDocument.Load(Path.Combine(root, "src", "ImageLabPlugin.Plugin", "ImageLabPlugin.Plugin.csproj"));
        Assert.Equal(new[] { "CommunityToolkit.Mvvm", "MyAvaloniaManagement.Plugin.Build", "MyAvaloniaManagement.PluginSdk", "MyAvaloniaManagement.PluginSdk.UI" },
            project.Descendants("PackageReference").Select(node => (string?)node.Attribute("Include")).ToArray());
    }

    private static ServiceProvider CreateProvider()
    {
        var registration = new TestRegistration();
        registration.Services.AddSingleton<IPluginWindowInteraction, NullWindowInteraction>();
        registration.Services.AddScoped<IDocumentLifetime, TestLifetime>();
        new ImageLabPluginModule().Configure(registration);
        return registration.Services.BuildServiceProvider(validateScopes: true);
    }

    private static string ReadAll(string directory) => string.Join('\n', Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(File.ReadAllText));
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null) { if (File.Exists(Path.Combine(directory.FullName, "ImageLabPlugin.slnx"))) return directory.FullName; directory = directory.Parent; }
        throw new DirectoryNotFoundException("未找到 ImageLabPlugin.slnx。");
    }

    private sealed class TestRegistration : IPluginRegistration
    {
        public PluginId PluginId => PluginIds.Plugin;
        public IServiceCollection Services { get; } = new ServiceCollection();
        public void UseLifecycle<TLifecycle>() where TLifecycle : class, IPluginLifecycle => Services.AddSingleton<TLifecycle>();
        public void AddDocument<TDocument, TView>(DocumentDescriptor descriptor) where TDocument : class, IPluginDocument where TView : Control, new() => Services.AddScoped<TDocument>();
        public void AddPersistableDocument<TDocument, TView>(DocumentDescriptor descriptor) where TDocument : class, IPersistablePluginDocument where TView : Control, new() { Services.AddScoped<TDocument>(); Services.AddTransient<TView>(); }
        public void AddTool<TTool, TView>(ToolDescriptor descriptor) where TTool : class where TView : Control, new() => throw new InvalidOperationException();
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
