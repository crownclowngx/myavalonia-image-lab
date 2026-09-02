using System.Xml.Linq;
using Xunit;

namespace ImageLabPlugin.Tests;

/// <summary>SOLID 依赖方向、朴素模式、中文设计注释和非发布阶段边界门禁。</summary>
public sealed class FrequencyMaskArchitectureTests
{
    [Fact]
    public void 领域层不依赖Avalonia基础设施Json或文件系统()
    {
        var root = FindRepositoryRoot();
        var directory = Path.Combine(root, "src", "ImageLabPlugin.Plugin", "Domain", "FrequencyMaskEditing");
        var source = ReadAll(directory);
        Assert.DoesNotContain("using Avalonia", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure", source, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Text.Json", source, StringComparison.Ordinal);
        Assert.DoesNotContain("File.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EventBus", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IServiceProvider", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Document不实现FFT共轭光栅或JsonDto循环()
    {
        var path = Path.Combine(FindRepositoryRoot(), "src", "ImageLabPlugin.Plugin", "Features",
            "FrequencyMaskEditor", "FrequencyMaskEditorDocument.cs");
        var source = File.ReadAllText(path);
        Assert.DoesNotContain("Fft2DTransform", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FrequencyCoordinates", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ConjugateMaskWriter", source, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonDocument", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetService", source, StringComparison.Ordinal);
    }

    [Fact]
    public void 核心新增文件包含中文设计说明且未新增产品Nuget()
    {
        var root = FindRepositoryRoot();
        var files = new[]
        {
            "Domain/Shared/Spectral/FrequencyGainMask.cs",
            "Domain/Shared/Spectral/FrequencyMaskApplier.cs",
            "Domain/FrequencyMaskEditing/FrequencyMaskModels.cs",
            "Domain/FrequencyMaskEditing/FrequencyMaskRasterizer.cs",
            "Application/FrequencyMaskEditing/FrequencyMaskEditorUseCases.cs",
            "Features/FrequencyMaskEditor/FrequencyMaskEditorDocument.cs"
        };
        foreach (var relative in files)
        {
            var source = File.ReadAllText(Path.Combine(root, "src", "ImageLabPlugin.Plugin", relative.Replace('/', Path.DirectorySeparatorChar)));
            Assert.Contains("/// <summary>", source, StringComparison.Ordinal);
            Assert.Matches("[\\u4e00-\\u9fff]", source);
        }

        var project = XDocument.Load(Path.Combine(root, "src", "ImageLabPlugin.Plugin", "ImageLabPlugin.Plugin.csproj"));
        var packages = project.Descendants("PackageReference").Select(node => (string?)node.Attribute("Include")).ToArray();
        Assert.Equal(new[] { "CommunityToolkit.Mvvm", "MyAvaloniaManagement.Plugin.Build", "MyAvaloniaManagement.PluginSdk", "MyAvaloniaManagement.PluginSdk.UI" }, packages);
    }

    [Fact]
    public void 未增加AiflowWindowsCi或发布配置()
    {
        var root = FindRepositoryRoot();
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
}
