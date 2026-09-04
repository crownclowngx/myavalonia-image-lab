using System.Xml.Linq;
using Xunit;

namespace ImageLabPlugin.Tests;

/// <summary>锁定 Poisson 的 SOLID 依赖方向、朴素模式、中文注释与本地非发布边界。</summary>
public sealed class PoissonArchitectureTests
{
    [Fact]
    public void Domain不依赖Avalonia应用基础设施DocumentJson或DI()
    {
        var source = ReadAll(Path.Combine(Root(), "src", "ImageLabPlugin.Plugin", "Domain", "PoissonBlending"));
        Assert.DoesNotContain("using Avalonia", source, StringComparison.Ordinal); Assert.DoesNotContain("Application.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure", source, StringComparison.Ordinal); Assert.DoesNotContain("System.Text.Json", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IServiceProvider", source, StringComparison.Ordinal); Assert.DoesNotContain("EventBus", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Mediator", source, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("Repository", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Document不实现guidance方程或sweep循环()
    {
        var source = File.ReadAllText(Path.Combine(Root(), "src", "ImageLabPlugin.Plugin", "Features", "PoissonBlending", "PoissonBlendingDocument.cs"));
        Assert.DoesNotContain("IPoissonGuidanceStrategy", source, StringComparison.Ordinal); Assert.DoesNotContain("NeighborIndices", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WritableRgba", source, StringComparison.Ordinal); Assert.DoesNotContain("GetRequiredService", source, StringComparison.Ordinal);
    }

    [Fact]
    public void 核心文件有中文设计注释且没有新增Nuget()
    {
        var root = Root(); var files = Directory.EnumerateFiles(Path.Combine(root, "src", "ImageLabPlugin.Plugin", "Domain", "PoissonBlending"), "*.cs")
            .Concat([Path.Combine(root, "src", "ImageLabPlugin.Plugin", "Application", "PoissonBlending", "PoissonBlendingSession.cs"), Path.Combine(root, "src", "ImageLabPlugin.Plugin", "Features", "PoissonBlending", "PoissonBlendingDocument.cs")]);
        foreach (var file in files) { var source = File.ReadAllText(file); Assert.Contains("/// <summary>", source, StringComparison.Ordinal); Assert.Matches("[\\u4e00-\\u9fff]", source); }
        var project = XDocument.Load(Path.Combine(root, "src", "ImageLabPlugin.Plugin", "ImageLabPlugin.Plugin.csproj")); Assert.Equal(4, project.Descendants("PackageReference").Count());
    }

    [Fact]
    public void 没有AiflowWindowsWorkflow或发布门禁()
    {
        var root = Root(); var production = ReadAll(Path.Combine(root, "src"));
        Assert.DoesNotContain("AIFLOW", production, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WorkbenchCommand", production, StringComparison.OrdinalIgnoreCase); Assert.False(Directory.Exists(Path.Combine(root, ".github", "workflows")));
    }

    private static string ReadAll(string directory) => string.Join('\n', Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories).Order().Select(File.ReadAllText));
    private static string Root() { var directory = new DirectoryInfo(AppContext.BaseDirectory); while (directory is not null) { if (File.Exists(Path.Combine(directory.FullName, "ImageLabPlugin.slnx"))) return directory.FullName; directory = directory.Parent; } throw new DirectoryNotFoundException(); }
}
