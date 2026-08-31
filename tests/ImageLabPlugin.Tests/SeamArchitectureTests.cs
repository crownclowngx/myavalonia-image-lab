using System.Xml.Linq;
using Xunit;

namespace ImageLabPlugin.Tests;

/// <summary>锁定 SOLID 依赖方向、朴素模式、中文注释和本地非发布边界。</summary>
public sealed class SeamArchitectureTests
{
    [Fact]
    public void Seam领域不依赖Avalonia应用基础设施Document或Json()
    {
        var root = FindRepositoryRoot();
        var source = ReadAll(Path.Combine(root, "src", "ImageLabPlugin.Plugin", "Domain", "SeamCarving"));
        Assert.DoesNotContain("using Avalonia", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Application.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure", source, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Text.Json", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IServiceProvider", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EventBus", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Document不实现Sobel动态规划或像素搬移循环()
    {
        var path = Path.Combine(FindRepositoryRoot(), "src", "ImageLabPlugin.Plugin", "Features", "SeamCarving", "SeamCarvingDocument.cs");
        var source = File.ReadAllText(path);
        Assert.DoesNotContain("SobelEnergyCalculator", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MinimumEnergySeamFinder", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WritableRgba", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetRequiredService", source, StringComparison.Ordinal);
    }

    [Fact]
    public void 核心文件具有中文设计说明且未新增Nuget()
    {
        var root = FindRepositoryRoot();
        var files = new[]
        {
            "Domain/SeamCarving/SeamCarvingModels.cs", "Domain/SeamCarving/SeamEnergyServices.cs",
            "Domain/SeamCarving/MinimumEnergySeamFinder.cs", "Domain/SeamCarving/SeamInsertionServices.cs",
            "Domain/SeamCarving/SeamPlanningServices.cs", "Domain/SeamCarving/ReferenceImageResamplers.cs",
            "Application/SeamCarving/SeamCarvingSession.cs", "Features/SeamCarving/SeamCarvingDocument.cs"
        };
        foreach (var relative in files)
        {
            var source = File.ReadAllText(Path.Combine(root, "src", "ImageLabPlugin.Plugin", relative.Replace('/', Path.DirectorySeparatorChar)));
            Assert.Contains("/// <summary>", source, StringComparison.Ordinal);
            Assert.Matches("[\\u4e00-\\u9fff]", source);
        }
        var project = XDocument.Load(Path.Combine(root, "src", "ImageLabPlugin.Plugin", "ImageLabPlugin.Plugin.csproj"));
        Assert.Equal(4, project.Descendants("PackageReference").Count());
    }

    [Fact]
    public void 未增加AiflowWindowsWorkflow或发布门禁()
    {
        var root = FindRepositoryRoot(); var production = ReadAll(Path.Combine(root, "src"));
        Assert.DoesNotContain("AIFLOW", production, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WorkflowAction", production, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WorkbenchCommand", production, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(root, ".github", "workflows")));
    }

    private static string ReadAll(string directory) => string.Join('\n', Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal).Select(File.ReadAllText));
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        { if (File.Exists(Path.Combine(directory.FullName, "ImageLabPlugin.slnx"))) return directory.FullName; directory = directory.Parent; }
        throw new DirectoryNotFoundException("未找到 ImageLabPlugin.slnx。");
    }
}
