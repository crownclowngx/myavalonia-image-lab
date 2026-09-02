using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ImageLabPlugin.Constants;
using ImageLabPlugin.Domain.MagnitudePhaseSwap;
using ImageLabPlugin.Features.MagnitudePhaseSwap;
using ImageLabPlugin.Plugin;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;
using Xunit;

namespace ImageLabPlugin.Tests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class MagnitudePhaseDocumentViewArchitectureTests
{
    [Fact]
    public void View可解析且Letterbox坐标映射明确区分内外()
    {
        var view = new MagnitudePhaseSwapView();
        Assert.True(typeof(MagnitudePhaseSwapView).IsSealed); Assert.NotNull(view);
        var center = MagnitudePhaseCoordinateMapper.ToNormalized(100, 50, 200, 100, 100, 100);
        Assert.True(center.IsInside); Assert.Equal(.5d, center.X, 12); Assert.Equal(.5d, center.Y, 12);
        var letterbox = MagnitudePhaseCoordinateMapper.ToNormalized(25, 50, 200, 100, 100, 100);
        Assert.False(letterbox.IsInside); Assert.Equal(0d, letterbox.X);
    }

    [Fact]
    public async Task 快照脱敏且恢复不自动读取或执行Fft()
    {
        using var provider = Provider();
        using var firstScope = provider.CreateScope();
        var document = firstScope.ServiceProvider.GetRequiredService<MagnitudePhaseSwapDocument>();
        await document.InitializeAsync(new NewDocumentActivation("幅度与相位交换"), default);
        document.PathA = @"C:\private\face-a.png"; document.PathB = @"D:\secret\building-b.jpg";
        document.CanvasSize = 1024; document.SelectedPreset = "相位 A→B（幅度 B）"; document.Amount = .75;
        var snapshot = await document.CaptureSaveSnapshotAsync(default);
        var json = snapshot.Content.Payload.GetRawText();
        Assert.Contains("face-a.png", json, StringComparison.Ordinal); Assert.Contains("building-b.jpg", json, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\private", json, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain(@"D:\secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rgba", json, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("spectrum", json, StringComparison.OrdinalIgnoreCase);

        using var secondScope = provider.CreateScope();
        var restored = secondScope.ServiceProvider.GetRequiredService<MagnitudePhaseSwapDocument>();
        await restored.InitializeAsync(new RestoreDocumentActivation("恢复幅相交换", snapshot.Content), default);
        Assert.Empty(restored.PathA); Assert.Empty(restored.PathB); Assert.Equal(1024, restored.CanvasSize);
        Assert.Equal("相位 A→B（幅度 B）", restored.SelectedPreset); Assert.Equal(.75d, restored.Amount);
        Assert.False(restored.HasInputs); Assert.False(restored.HasResult); Assert.False(restored.IsDirty);
        Assert.Contains("不会自动读取", restored.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 配方变化推进Revision而瞬时状态不进入保存内容()
    {
        using var provider = Provider(); using var scope = provider.CreateScope();
        var document = scope.ServiceProvider.GetRequiredService<MagnitudePhaseSwapDocument>();
        await document.InitializeAsync(new NewDocumentActivation("幅相交换"), default);
        Assert.False(document.IsDirty); document.Amount = .6d; Assert.False(document.IsDirty); // 当前非插值预设，t 不参与配方。
        document.SelectedPreset = "幅度 A→B（相位 A）"; Assert.True(document.IsDirty);
        var snapshot = await document.CaptureSaveSnapshotAsync(default); document.AcceptChanges(snapshot.Revision);
        Assert.False(document.IsDirty);
    }

    [Fact]
    public void 两个Scope隔离Document而共享无状态数值服务()
    {
        using var provider = Provider(); using var first = provider.CreateScope(); using var second = provider.CreateScope();
        var firstDocument = first.ServiceProvider.GetRequiredService<MagnitudePhaseSwapDocument>();
        var secondDocument = second.ServiceProvider.GetRequiredService<MagnitudePhaseSwapDocument>();
        Assert.NotSame(firstDocument, secondDocument);
        Assert.Same(first.ServiceProvider.GetRequiredService<SpectrumComponentMixer>(),
            second.ServiceProvider.GetRequiredService<SpectrumComponentMixer>());
    }

    [Fact]
    public void 领域依赖方向Document边界和朴素模式门禁成立()
    {
        var root = Root();
        var domain = ReadAll(Path.Combine(root, "src", "ImageLabPlugin.Plugin", "Domain", "MagnitudePhaseSwap"));
        Assert.DoesNotContain("using Avalonia", domain, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Text.Json", domain, StringComparison.Ordinal);
        Assert.DoesNotContain("IServiceProvider", domain, StringComparison.Ordinal);
        Assert.DoesNotContain("Features.", domain, StringComparison.Ordinal);
        Assert.DoesNotContain("AbstractFactory", domain, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Mediator", domain, StringComparison.OrdinalIgnoreCase);
        var production = ReadAll(Path.Combine(root, "src", "ImageLabPlugin.Plugin"));
        Assert.DoesNotContain("AIFLOW", production, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WorkflowAction", production, StringComparison.OrdinalIgnoreCase);
        var document = File.ReadAllText(Path.Combine(root, "src", "ImageLabPlugin.Plugin", "Features", "MagnitudePhaseSwap", "MagnitudePhaseSwapDocument.cs"));
        Assert.DoesNotContain("Fft2DTransform", document, StringComparison.Ordinal);
        Assert.DoesNotContain("SpectrumComponentMixer", document, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonSerializer", document, StringComparison.Ordinal);
        Assert.DoesNotContain("for (", document, StringComparison.Ordinal);
    }

    [Fact]
    public void 核心生产文件有中文设计注释且Nuget白名单不变()
    {
        var root = Root();
        var files = new[]
        {
            "Domain/MagnitudePhaseSwap/FrequencyPairCanvas.cs", "Domain/MagnitudePhaseSwap/FrequencyPairCanvasProjector.cs",
            "Domain/MagnitudePhaseSwap/MagnitudePhaseRecipe.cs", "Domain/MagnitudePhaseSwap/SpectrumComponentMixer.cs",
            "Domain/MagnitudePhaseSwap/MagnitudePhaseReconstructor.cs", "Domain/MagnitudePhaseSwap/MagnitudePhaseDisplayProjector.cs",
            "Domain/MagnitudePhaseSwap/MagnitudePhaseDiagnostics.cs", "Application/MagnitudePhaseSwap/MagnitudePhaseContracts.cs",
            "Application/MagnitudePhaseSwap/MagnitudePhaseSession.cs", "Application/MagnitudePhaseSwap/MagnitudePhaseUseCases.cs",
            "Infrastructure/Persistence/MagnitudePhaseSerializers.cs", "Features/MagnitudePhaseSwap/MagnitudePhaseSwapDocument.cs"
        };
        foreach (var relative in files)
        {
            var source = File.ReadAllText(Path.Combine(root, "src", "ImageLabPlugin.Plugin", relative.Replace('/', Path.DirectorySeparatorChar)));
            Assert.Contains("/// <summary>", source, StringComparison.Ordinal); Assert.Matches("[\u4e00-\u9fff]", source);
        }
        var project = XDocument.Load(Path.Combine(root, "src", "ImageLabPlugin.Plugin", "ImageLabPlugin.Plugin.csproj"));
        Assert.Equal(new[] { "CommunityToolkit.Mvvm", "MyAvaloniaManagement.Plugin.Build", "MyAvaloniaManagement.PluginSdk", "MyAvaloniaManagement.PluginSdk.UI" },
            project.Descendants("PackageReference").Select(node => (string?)node.Attribute("Include")).ToArray());
    }

    private static ServiceProvider Provider()
    {
        var registration = new Registration(); registration.Services.AddSingleton<IPluginWindowInteraction, NullWindowInteraction>();
        registration.Services.AddScoped<IDocumentLifetime, Lifetime>(); new ImageLabPluginModule().Configure(registration);
        return registration.Services.BuildServiceProvider(validateScopes: true);
    }

    private static string ReadAll(string directory) => string.Join('\n', Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(File.ReadAllText));
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null) { if (File.Exists(Path.Combine(directory.FullName, "ImageLabPlugin.slnx"))) return directory.FullName; directory = directory.Parent; }
        throw new DirectoryNotFoundException("未找到 ImageLabPlugin.slnx。");
    }

    private sealed class Registration : IPluginRegistration
    {
        public PluginId PluginId => PluginIds.Plugin; public IServiceCollection Services { get; } = new ServiceCollection();
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

    private sealed class Lifetime : IDocumentLifetime { public CancellationToken ClosingToken => CancellationToken.None; public bool IsClosing => false; }
}
