using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ImageLabPlugin.Application.PoissonBlending;
using ImageLabPlugin.Domain.PoissonBlending;

namespace ImageLabPlugin.Infrastructure.PoissonBlending;

/// <summary>
/// 把已经验证的实验事实写成固定 camelCase JSON 或带 UTF-8 BOM 的 CSV。报告只含 fingerprint、尺寸、
/// 拓扑、参数、有限残差和诊断，不含绝对路径、RGBA、遮罩栅格、RHS、解或迭代帧。
/// </summary>
internal sealed class PoissonBlendingReportSerializer : IPoissonBlendingReportSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public byte[] SerializeJson(PoissonBlendingReport report)
    {
        Validate(report);
        var payload = new
        {
            schema = PoissonProtocols.ReportSchema,
            product = "Poisson Blending／梯度域融合",
            numericProtocol = PoissonProtocols.Numeric,
            budgetProtocol = PoissonProtocols.Budget,
            report.CreatedAtUtc,
            mode = report.Mode.ToString(),
            source = new { report.SourceFingerprint, width = report.SourceSize.Width, height = report.SourceSize.Height },
            target = new { report.TargetFingerprint, width = report.TargetSize.Width, height = report.TargetSize.Height },
            offset = new { dx = report.Offset.Dx, dy = report.Offset.Dy },
            mask = new
            {
                report.Topology.UnknownCount,
                report.Topology.BoundingBox,
                report.Topology.ComponentCount,
                report.Topology.HoleCount,
                report.Topology.BoundaryCount
            },
            options = new
            {
                report.Options.RmsTolerance,
                report.Options.MaxAbsTolerance,
                report.Options.MaxIterations,
                report.Options.PreviewInterval
            },
            resource = report.ResourceEstimate,
            convergence = new
            {
                stopReason = report.StopReason.ToString(),
                iterationCount = report.Residuals[^1].Iteration,
                initial = report.Residuals[0],
                final = report.Residuals[^1],
                bestRms = report.Residuals.Min(item => item.Rms),
                sampleCount = report.Residuals.Count
            },
            diagnostics = new
            {
                report.Diagnostics.BoundaryGuidanceRmse,
                report.Diagnostics.InteriorGradientRmse,
                report.Diagnostics.ResidualRms,
                report.Diagnostics.ResidualMaxAbs,
                mixedSourceEdgeRatio = report.Diagnostics.MixedSourceEdgeRatio,
                report.Diagnostics.ClampStatistics.ClippedChannelCount,
                report.Diagnostics.ClampStatistics.ClippedPixelCount
            },
            interpretation = "残差与梯度误差描述数值目标，不等于主观视觉质量，也不构成算法排名。",
            report.Warnings
        };
        return JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
    }

    public byte[] SerializeCsv(PoissonBlendingReport report)
    {
        Validate(report);
        var builder = new StringBuilder("iteration,rms,maxAbs,relativeRms,stopReason\r\n");
        foreach (var item in report.Residuals.Take(2_001))
        {
            string[] values = [Format(item.Iteration), Format(item.Rms), Format(item.MaxAbs), Format(item.RelativeRms),
                item.Iteration == report.Residuals[^1].Iteration ? report.StopReason.ToString() : "N/A"];
            builder.AppendJoin(',', values.Select(Escape)).Append("\r\n");
        }
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        var body = encoding.GetBytes(builder.ToString()); var preamble = encoding.GetPreamble();
        var result = new byte[preamble.Length + body.Length]; preamble.CopyTo(result, 0); body.CopyTo(result, preamble.Length); return result;
    }

    private static void Validate(PoissonBlendingReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.Residuals.Count is 0 or > 2_001) throw new ArgumentException("残差序列必须包含 1 至 2,001 条。", nameof(report));
        if (report.Residuals.Any(item => !double.IsFinite(item.Rms) || !double.IsFinite(item.MaxAbs) || !double.IsFinite(item.RelativeRms)))
            throw new ArgumentException("报告不允许 NaN 或 Infinity。", nameof(report));
        if (!Enum.IsDefined(report.Mode) || !Enum.IsDefined(report.StopReason)) throw new ArgumentException("报告包含未知枚举。", nameof(report));
    }

    private static string Format<T>(T value) where T : IFormattable => value.ToString(null, CultureInfo.InvariantCulture);
    private static string Escape(string value) => value.IndexOfAny([',', '"', '\r', '\n']) < 0 ? value : $"\"{value.Replace("\"", "\"\"")}\"";
}
