using System.Xml.Linq;
using Xunit;

namespace ImageLabPlugin.Tests;

/// <summary>以源码门禁固定 Spectral Art 的 SOLID 依赖方向、朴素模式和非发布边界。</summary>
public sealed class SpectralArtArchitectureTests
{
    [Fact]
    public void 领域层不依赖AvaloniaJson文件系统DI或其他Feature()
    {
        var source = ReadAll(Path.Combine(Root(), "src", "ImageLabPlugin.Plugin", "Domain", "SpectralArt"));
        Assert.DoesNotContain("using Avalonia", source, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Text.Json", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IServiceProvider", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Infrastructure", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Features.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FrequencyMaskEditing", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PeriodicNoiseRemoval", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Document不包含ComplexFFT共轭公式像素循环或Json解析()
    {
        var source = File.ReadAllText(Path.Combine(Root(), "src", "ImageLabPlugin.Plugin", "Features", "SpectralArt", "SpectralArtDocument.cs"));
        Assert.DoesNotContain("System.Numerics", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Complex[]", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Fft2DTransform", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ConjugateIndex", source, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonDocument", source, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonSerializer", source, StringComparison.Ordinal);
        Assert.DoesNotContain("for (", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IServiceProvider", source, StringComparison.Ordinal);
    }

    [Fact]
    public void 核心生产文件有中文设计注释且未增加Nuget()
    {
        var root = Root();
        var files = new[] { "Domain/SpectralArt/SpectralPattern.cs", "Domain/SpectralArt/SpectralPatternMapper.cs",
            "Domain/SpectralArt/SpectralAmplitudeWriter.cs", "Domain/SpectralArt/SpectralArtReconstructor.cs",
            "Domain/SpectralArt/SpectralArtDiagnostics.cs", "Domain/SpectralArt/SpectralPatternPreviewProjector.cs",
            "Domain/SpectralArt/SpectralExportFactVerifier.cs", "Application/SpectralArt/SpectralArtContracts.cs",
            "Application/SpectralArt/SpectralArtUseCases.cs", "Infrastructure/Persistence/SpectralArtSerializers.cs",
            "Infrastructure/Imaging/AvaloniaSpectralTextRasterizer.cs", "Features/SpectralArt/SpectralArtDocument.cs",
            "Features/SpectralArt/SpectralArtRegionCanvas.cs" };
        foreach (var relative in files)
        {
            var source = File.ReadAllText(Path.Combine(root, "src", "ImageLabPlugin.Plugin", relative.Replace('/', Path.DirectorySeparatorChar)));
            Assert.Contains("/// <summary>", source, StringComparison.Ordinal); Assert.Matches("[\\u4e00-\\u9fff]", source);
        }
        var project = XDocument.Load(Path.Combine(root, "src", "ImageLabPlugin.Plugin", "ImageLabPlugin.Plugin.csproj"));
        Assert.Equal(new[] { "CommunityToolkit.Mvvm", "MyAvaloniaManagement.Plugin.Build", "MyAvaloniaManagement.PluginSdk", "MyAvaloniaManagement.PluginSdk.UI" },
            project.Descendants("PackageReference").Select(node => (string?)node.Attribute("Include")).ToArray());
    }

    [Fact]
    public void 未增加WindowsCI发布门禁或Aiflow生产依赖()
    {
        var root = Root(); var production = ReadAll(Path.Combine(root, "src"));
        Assert.DoesNotContain("AIFLOW", production, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(root, ".github", "workflows")));
        Assert.Empty(Directory.EnumerateFiles(root, "*.yml", SearchOption.TopDirectoryOnly));
        Assert.Empty(Directory.EnumerateFiles(root, "*.yaml", SearchOption.TopDirectoryOnly));
    }

    private static string ReadAll(string directory) => string.Join('\n', Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(File.ReadAllText));
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null) { if (File.Exists(Path.Combine(directory.FullName, "ImageLabPlugin.slnx"))) return directory.FullName; directory = directory.Parent; }
        throw new DirectoryNotFoundException("未找到 ImageLabPlugin.slnx。");
    }
}
