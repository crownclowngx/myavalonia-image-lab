using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using ImageLabPlugin.Application.ColorTransfer;
using ImageLabPlugin.Domain.ColorTransfer;
using ImageLabPlugin.Domain.Imaging;
using ImageLabPlugin.Domain.Comparison;
using ImageLabPlugin.Infrastructure.ColorTransfer;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class ColorTransferReportAndArchitectureTests
{
    [Fact]
    public void Json报告版本化不包含路径像素机器隐私且N用Null状态表达()
    {
        var serializer = new ColorTransferReportSerializer(); var report = Report();
        var bytes = serializer.Serialize(report, ColorReportFormat.Json); var text = Encoding.UTF8.GetString(bytes);
        using var json = JsonDocument.Parse(bytes);
        Assert.Equal(ColorTransferProtocols.ReportSchema, json.RootElement.GetProperty("schema").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("target").GetProperty("circularMeanHueDegrees").ValueKind);
        Assert.Contains("not-applicable", text, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rgba", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.MachineName, text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Csv报告有Utf8BOM稳定列和调色板逐项记录()
    {
        var bytes = new ColorTransferReportSerializer().Serialize(Report(), ColorReportFormat.Csv);
        Assert.True(bytes.Take(Encoding.UTF8.GetPreamble().Length).SequenceEqual(Encoding.UTF8.GetPreamble()));
        var text = Encoding.UTF8.GetString(bytes); Assert.Contains("recordType,key,value,status", text, StringComparison.Ordinal);
        Assert.Contains("\"palette\",\"cluster-0\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void 报告严格拒绝NaN与Infinity()
    {
        var invalid = Report() with { Difference = Report().Difference with { Maximum = double.PositiveInfinity } };
        Assert.Throws<InvalidDataException>(() => new ColorTransferReportSerializer().Serialize(invalid, ColorReportFormat.Json));
    }

    [Fact]
    public void Domain不依赖AvaloniaJson文件对话框DI且生产代码无Aiflow与WindowsCI()
    {
        var root = Root(); var imagingRoot = Path.Combine(root, "src", "ImageLabPlugin.Plugin", "Domain", "Imaging");
        var domain = ReadAll(Path.Combine(root, "src", "ImageLabPlugin.Plugin", "Domain", "ColorTransfer")) +
            string.Join('\n', new[] { "SrgbColorSpace.cs", "CieLabColorSpace.cs", "HsvColorSpace.cs", "CieDeltaE.cs" }
                .Select(name => File.ReadAllText(Path.Combine(imagingRoot, name))));
        Assert.DoesNotContain("Avalonia", domain, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Text.Json", domain, StringComparison.Ordinal);
        Assert.DoesNotContain("File.", domain, StringComparison.Ordinal);
        Assert.DoesNotContain("IServiceProvider", domain, StringComparison.Ordinal);
        var production = ReadAll(Path.Combine(root, "src"));
        Assert.DoesNotContain("AIFLOW", production, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WorkflowAction", production, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WorkbenchCommand", production, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(root, ".github", "workflows")));
    }

    [Fact]
    public void 新增生产核心都有中文设计注释且未新增Nuget()
    {
        var root = Root(); var files = new[]
        {
            "Domain/Imaging/SrgbColorSpace.cs", "Domain/Imaging/CieLabColorSpace.cs", "Domain/Imaging/HsvColorSpace.cs",
            "Domain/Imaging/CieDeltaE.cs", "Domain/ColorTransfer/ColorDistributionAnalyzer.cs",
            "Domain/ColorTransfer/RgbColorAggregator.cs", "Domain/ColorTransfer/DominantColorClusterer.cs",
            "Domain/ColorTransfer/LabStatisticsTransfer.cs", "Domain/ColorTransfer/FixedPaletteRemapper.cs",
            "Application/ColorTransfer/ColorTransferSession.cs", "Features/PaletteColorTransfer/PaletteColorTransferDocument.cs",
            "Infrastructure/ColorTransfer/ColorTransferReportSerializer.cs"
        };
        foreach (var relative in files)
        {
            var source = File.ReadAllText(Path.Combine(root, "src", "ImageLabPlugin.Plugin", relative.Replace('/', Path.DirectorySeparatorChar)));
            Assert.Contains("/// <summary>", source, StringComparison.Ordinal); Assert.Matches("[\\u4e00-\\u9fff]", source);
        }
        var project = XDocument.Load(Path.Combine(root, "src", "ImageLabPlugin.Plugin", "ImageLabPlugin.Plugin.csproj"));
        Assert.Equal(4, project.Descendants("PackageReference").Count());
    }

    private static ColorExperimentReport Report()
    {
        var channel = new ChannelStatistics(0, 0, 0, 0, 0);
        var stats = new ColorStatistics(1, 1, 1, new SrgbColor(0.5, 0.5, 0.5), new CieLabColor(50, 0, 0),
            new CieLabColor(0, 0, 0), channel, channel, channel, null, 0, 0, 1);
        var color = new SrgbColor(1, 0, 0); var entry = new PaletteEntry(0, color, ColorTransferTestFactory.ToLab(color), 1, 1, 0, 0);
        var palette = new FrozenPalette("source", [entry], PaletteSource.Target, "frozen");
        var image = ColorTransferTestFactory.Image(1, 1, 128, 128, 128, 255);
        var quality = new FullReferenceQualityAnalyzer(new ImagePairValidator()).Analyze(image, image);
        return new ColorExperimentReport(ColorOperationKind.FixedPaletteRemap, "recipe", new ImageSize(1, 1), null,
            stats, null, stats, palette, new DifferenceSummary(1, 1.5, 2.5, 3, 1, new double[100]),
            new GamutMappingDiagnostics(1, 0, 0, 0), quality, null, null);
    }

    private static string ReadAll(string directory) => string.Join('\n', Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
    private static string Root()
    { var directory = new DirectoryInfo(AppContext.BaseDirectory); while (directory is not null) { if (File.Exists(Path.Combine(directory.FullName, "ImageLabPlugin.slnx"))) return directory.FullName; directory = directory.Parent; } throw new DirectoryNotFoundException(); }
}
