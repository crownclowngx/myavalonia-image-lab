using System.Xml.Linq;
using Xunit;

namespace ImageLabPlugin.Tests;

/// <summary>SVD 产品的 SOLID 依赖方向、朴素模式、中文注释与非发布阶段门禁。</summary>
public sealed class SvdArchitectureTests
{
    [Fact]
    public void Svd领域层不依赖Avalonia应用基础设施Json文件系统或DI()
    {
        var source = ReadAll(Path.Combine(Root(), "src", "ImageLabPlugin.Plugin", "Domain", "SvdDecomposition"));
        Assert.DoesNotContain("using Avalonia", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Application", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure", source, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Text.Json", source, StringComparison.Ordinal);
        Assert.DoesNotContain("File.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IServiceProvider", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Mediator", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EventBus", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Document不实现Jacobi矩阵乘法颜色循环或Json写入()
    {
        var source = File.ReadAllText(Path.Combine(Root(), "src", "ImageLabPlugin.Plugin", "Features",
            "SvdDecomposition", "SvdDecompositionDocument.cs"));
        Assert.DoesNotContain("RotateColumns", source, StringComparison.Ordinal);
        Assert.DoesNotContain("JacobiSvdDecomposer", source, StringComparison.Ordinal);
        Assert.DoesNotContain("YCbCrColorSpace", source, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonDocument", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetService", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Svd应用层不依赖AvaloniaFeature或Infrastructure具体实现()
    {
        var source = ReadAll(Path.Combine(Root(), "src", "ImageLabPlugin.Plugin", "Application", "SvdDecomposition"));
        Assert.DoesNotContain("using Avalonia", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Features.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IServiceProvider", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ISvdService", source, StringComparison.Ordinal);
    }

    [Fact]
    public void 核心生产文件都有中文设计说明且产品Nuget白名单不变()
    {
        var root = Root();
        string[] files =
        [
            "Domain/Shared/Imaging/ImageAreaResampler.cs",
            "Domain/SvdDecomposition/SvdModels.cs",
            "Domain/SvdDecomposition/JacobiSvdDecomposer.cs",
            "Domain/SvdDecomposition/SvdAnalysis.cs",
            "Domain/SvdDecomposition/SvdColorProcessing.cs",
            "Application/SvdDecomposition/SvdContracts.cs",
            "Application/SvdDecomposition/SvdUseCases.cs",
            "Infrastructure/Persistence/SvdReportSerializer.cs",
            "Features/SvdDecomposition/SvdDecompositionDocument.cs",
            "Features/SvdDecomposition/SingularValueCurveControl.cs"
        ];
        foreach (var relative in files)
        {
            var source = File.ReadAllText(Path.Combine(root, "src", "ImageLabPlugin.Plugin",
                relative.Replace('/', Path.DirectorySeparatorChar)));
            Assert.Contains("/// <summary>", source, StringComparison.Ordinal);
            Assert.Matches("[\\u4e00-\\u9fff]", source);
        }
        var project = XDocument.Load(Path.Combine(root, "src", "ImageLabPlugin.Plugin", "ImageLabPlugin.Plugin.csproj"));
        var packages = project.Descendants("PackageReference").Select(node => (string?)node.Attribute("Include")).ToArray();
        Assert.Equal(new[] { "CommunityToolkit.Mvvm", "MyAvaloniaManagement.Plugin.Build",
            "MyAvaloniaManagement.PluginSdk", "MyAvaloniaManagement.PluginSdk.UI" }, packages);
    }

    [Fact]
    public void 未增加AiflowWindowsCi发布配置或误导压缩字段()
    {
        var root = Root();
        var production = ReadAll(Path.Combine(root, "src"));
        Assert.DoesNotContain("AIFLOW", production, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WorkbenchCommand", production, StringComparison.OrdinalIgnoreCase);
        var svd = ReadAll(Path.Combine(root, "src", "ImageLabPlugin.Plugin"));
        Assert.DoesNotContain("CompressionRatio", svd, StringComparison.Ordinal);
        Assert.DoesNotContain("CompressedBytes", svd, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveSpace", svd, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(root, ".github", "workflows")));
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
