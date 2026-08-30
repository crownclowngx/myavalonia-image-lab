using System.Xml.Linq;
using Xunit;

namespace ImageLabPlugin.Tests;

/// <summary>周期噪声产品的 SOLID 依赖方向、朴素模式、中文注释和非发布边界门禁。</summary>
public sealed class PeriodicNoiseArchitectureTests
{
    [Fact]
    public void 领域层不依赖Avalonia基础设施Json文件系统或DI()
    {
        var source = ReadAll(Path.Combine(Root(), "src", "ImageLabPlugin.Plugin", "Domain",
            "PeriodicNoiseRemoval"));
        Assert.DoesNotContain("using Avalonia", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure", source, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Text.Json", source, StringComparison.Ordinal);
        Assert.DoesNotContain("File.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IServiceProvider", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EventBus", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Mediator", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Document不实现FFT检测Notch公式共轭索引或Json循环()
    {
        var path = Path.Combine(Root(), "src", "ImageLabPlugin.Plugin", "Features",
            "PeriodicNoiseRemoval", "PeriodicNoiseRemovalDocument.cs");
        var source = File.ReadAllText(path);
        Assert.DoesNotContain("Fft2DTransform", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FrequencyCoordinates", source, StringComparison.Ordinal);
        Assert.DoesNotContain("NotchResponse", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RadialSpectrumBaseline", source, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonDocument", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetService", source, StringComparison.Ordinal);
    }

    [Fact]
    public void 核心生产类型都有中文设计注释且产品Nuget白名单不变()
    {
        var root = Root();
        var files = new[]
        {
            "Domain/PeriodicNoiseRemoval/PeriodicNoiseModels.cs",
            "Domain/PeriodicNoiseRemoval/RadialSpectrumBaseline.cs",
            "Domain/PeriodicNoiseRemoval/PeriodicPeakDetector.cs",
            "Domain/PeriodicNoiseRemoval/NotchMaskFactory.cs",
            "Domain/PeriodicNoiseRemoval/PeriodicNoiseLossAnalyzer.cs",
            "Application/PeriodicNoiseRemoval/PeriodicNoiseContracts.cs",
            "Application/PeriodicNoiseRemoval/PeriodicNoiseUseCases.cs",
            "Infrastructure/Persistence/PeriodicNoiseRecipeSerializer.cs",
            "Features/PeriodicNoiseRemoval/PeriodicNoiseRemovalDocument.cs",
            "Features/PeriodicNoiseRemoval/PeriodicSpectrumControl.cs"
        };
        foreach (var relative in files)
        {
            var source = File.ReadAllText(Path.Combine(root, "src", "ImageLabPlugin.Plugin",
                relative.Replace('/', Path.DirectorySeparatorChar)));
            Assert.Contains("/// <summary>", source, StringComparison.Ordinal);
            Assert.Matches("[\\u4e00-\\u9fff]", source);
        }
        var project = XDocument.Load(Path.Combine(root, "src", "ImageLabPlugin.Plugin",
            "ImageLabPlugin.Plugin.csproj"));
        var packages = project.Descendants("PackageReference").Select(node =>
            (string?)node.Attribute("Include")).ToArray();
        Assert.Equal(new[] { "CommunityToolkit.Mvvm", "MyAvaloniaManagement.Plugin.Build",
            "MyAvaloniaManagement.PluginSdk", "MyAvaloniaManagement.PluginSdk.UI" }, packages);
    }

    [Fact]
    public void 未引入AiflowWindowsCi工作流或发布配置()
    {
        var root = Root();
        var production = ReadAll(Path.Combine(root, "src"));
        Assert.DoesNotContain("AIFLOW", production, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WorkflowAction", production, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WorkbenchCommand", production, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(root, ".github", "workflows")));
        Assert.Empty(Directory.EnumerateFiles(root, "*.yml", SearchOption.TopDirectoryOnly));
        Assert.Empty(Directory.EnumerateFiles(root, "*.yaml", SearchOption.TopDirectoryOnly));
    }

    private static string ReadAll(string directory) => string.Join('\n',
        Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal).Select(File.ReadAllText));

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ImageLabPlugin.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("未找到 ImageLabPlugin.slnx。");
    }
}
