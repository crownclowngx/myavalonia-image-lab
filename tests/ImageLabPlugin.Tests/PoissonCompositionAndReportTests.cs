using System.Text;
using System.Text.Json;
using ImageLabPlugin.Application.PoissonBlending;
using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Domain.PoissonBlending;
using ImageLabPlugin.Infrastructure.PoissonBlending;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class PoissonCompositionAndReportTests
{
    [Fact]
    public void 直接Alpha域外逐字节不变且域内不透明时硬克隆()
    {
        var source = PoissonTestFactory.Solid(5, 5, 255, 0, 0); var target = PoissonTestFactory.Solid(7, 7, 0, 0, 255);
        var mask = PoissonTestFactory.RectangleMask(5, 5, new(2, 2, 1, 1));
        var output = new DirectAlphaCompositor(new SrgbColorSpace()).Compose(source, target, mask, new(1, 1));
        Assert.Equal((byte)255, output.GetPixel(3, 3).R); Assert.Equal((byte)255, output.GetPixel(0, 0).B);
        Assert.Equal(target.GetPixel(0, 0), output.GetPixel(0, 0));
    }

    [Fact]
    public void RGB合成统计上下色域裁剪并保留目标Alpha()
    {
        var target = PoissonTestFactory.Solid(5, 5, 0, 0, 0); var topology = new PoissonMaskTopology(1, new(2, 2, 1, 1), 1, 0, 1);
        var resource = new PoissonResourceEstimate(1, 1, 3, 1, 1, []);
        var problem = new PoissonProblem("p", PoissonBlendMode.NormalClone, target.Size, [2], [2], [2], [2], [-1, -1, -1, -1], [0d, 0d, 0d], [0d, 0d, 0d], topology, resource, 0, 0);
        var state = new PoissonSolverState("p", [-.1, .5, 1.2], new(0, 1, 1, 1));
        var result = new PoissonBlendComposer(new SrgbColorSpace()).Compose(target, problem, state);
        Assert.Equal(2, result.ClampStatistics.ClippedChannelCount); Assert.Equal(1, result.ClampStatistics.ClippedPixelCount);
        Assert.Equal((byte)255, result.Image.GetAlpha(2, 2)); Assert.Equal((byte)0, result.Image.GetPixel(2, 2).R); Assert.Equal((byte)255, result.Image.GetPixel(2, 2).B);
    }

    [Fact]
    public void 资源预算对边界内外和标量更新结构化阻断()
    {
        var estimator = new PoissonResourceEstimator(); var topology = new PoissonMaskTopology(100, new(0, 0, 10, 10), 1, 0, 10);
        Assert.True(estimator.Estimate(new(100, 100), new(100, 100), topology, 3, 10).IsAllowed);
        var tooMany = new PoissonMaskTopology(500_001, new(0, 0, 1000, 501), 1, 0, 1);
        var result = estimator.Estimate(new(100, 100), new(100, 100), tooMany, 3, 2000);
        Assert.False(result.IsAllowed); Assert.Contains(result.BlockingReasons, text => text.Contains("未知量", StringComparison.Ordinal));
        Assert.Contains(result.BlockingReasons, text => text.Contains("标量更新", StringComparison.Ordinal));
    }

    [Fact]
    public void Guidance和Rhs显示投影不修改数值输入()
    {
        var source = PoissonTestFactory.Gradient(6, 6); var target = PoissonTestFactory.Solid(8, 8, 30, 40, 50);
        var mask = PoissonTestFactory.RectangleMask(6, 6, new(2, 2, 2, 2)); var catalog = PoissonTestFactory.Catalog();
        var problem = PoissonTestFactory.Builder().Build(source, target, mask, new(1, 1), new(PoissonBlendMode.MixedGradient));
        var rhs = (double[])problem.Rhs.Clone(); var projector = new PoissonFieldProjector(new SrgbColorSpace(), catalog);
        var guidance = projector.ProjectGuidance(source, target, mask, new(1, 1), PoissonBlendMode.MixedGradient);
        var rhsImage = projector.ProjectRhs(problem);
        Assert.Equal(target.Size, guidance.Size); Assert.Equal(target.Size, rhsImage.Size); Assert.Equal(rhs, problem.Rhs);
    }

    [Fact]
    public void JSON和CSV报告不泄露路径像素遮罩或非有限值()
    {
        var report = CreateReport(); var serializer = new PoissonBlendingReportSerializer();
        var jsonBytes = serializer.SerializeJson(report); var json = Encoding.UTF8.GetString(jsonBytes);
        using var parsed = JsonDocument.Parse(jsonBytes);
        Assert.Equal(PoissonProtocols.ReportSchema, parsed.RootElement.GetProperty("schema").GetString());
        Assert.DoesNotContain("C:\\private", json, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("rgba", json, StringComparison.OrdinalIgnoreCase);
        var csv = serializer.SerializeCsv(report); Assert.True(csv.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        Assert.Contains("iteration,rms,maxAbs,relativeRms,stopReason", Encoding.UTF8.GetString(csv), StringComparison.Ordinal);
    }

    [Fact]
    public void 报告拒绝NaN且混合比例其他模式使用null而非伪造零()
    {
        var report = CreateReport(); Assert.Null(report.Diagnostics.MixedSourceEdgeRatio);
        var invalid = report with { Residuals = [new(0, double.NaN, 0, 0)] };
        Assert.Throws<ArgumentException>(() => new PoissonBlendingReportSerializer().SerializeJson(invalid));
    }

    private static PoissonBlendingReport CreateReport()
    {
        var topology = new PoissonMaskTopology(1, new(2, 2, 1, 1), 1, 0, 1);
        var options = new PoissonBlendOptions(PoissonBlendMode.NormalClone); var resource = new PoissonResourceEstimate(1, 1, 3, 3, 1024, []);
        var diagnostics = new PoissonBlendDiagnostics(0, 0, 0, 0, null, new(0, 0));
        return new("SOURCE", "TARGET", new(5, 5), new(5, 5), PoissonBlendMode.NormalClone, default, topology, options,
            resource, [new(0, 1, 1, 1), new(1, 0, 0, 0)], PoissonStopReason.Converged, diagnostics, DateTimeOffset.UnixEpoch, []);
    }
}
