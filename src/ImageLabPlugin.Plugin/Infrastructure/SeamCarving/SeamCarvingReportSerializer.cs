using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ImageLabPlugin.Application.SeamCarving;
using ImageLabPlugin.Domain.Shared.Analysis;
using ImageLabPlugin.Domain.SeamCarving;

namespace ImageLabPlugin.Infrastructure.SeamCarving;

/// <summary>把已经验证的运行事实序列化为固定 camelCase JSON 或带 BOM 的 RFC 4180 CSV。</summary>
/// <remarks>
/// DTO 故意不含源路径、RGBA、蒙版栅格、能量矩阵和路径坐标。PSNR 正无穷用 isExact=true 与 null 数值表达，
/// 不把 Infinity/NaN 偷渡进 JSON。CSV 使用 InvariantCulture，避免系统区域改变小数点或列结构。
/// </remarks>
internal sealed class SeamCarvingReportSerializer : ISeamCarvingReportSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public byte[] SerializeJson(SeamCarvingReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var payload = new
        {
            schema = SeamCarvingProtocols.ReportSchema,
            product = "Seam Carving／内容感知缩放",
            energyProtocol = SeamCarvingProtocols.Energy,
            interpolationProtocol = SeamCarvingProtocols.Interpolation,
            budgetProtocol = SeamCarvingProtocols.Budget,
            report.CreatedAtUtc,
            input = new { report.InputFingerprint, width = report.InputSize.Width, height = report.InputSize.Height },
            target = new { width = report.TargetSize.Width, height = report.TargetSize.Height },
            axisOrder = report.AxisOrder.ToString(),
            referenceAlgorithm = report.ReferenceAlgorithm.ToString(),
            status = report.Status.ToString(),
            mask = new { report.MaskCounts.Normal, report.MaskCounts.Protect, report.MaskCounts.PreferRemoval },
            resource = report.ResourceEstimate,
            steps = report.Steps.Take(SeamResourceEstimator.MaximumTotalSeams).Select(item => new
            {
                item.StepNumber,
                orientation = item.Orientation.ToString(),
                operation = item.Operation.ToString(),
                before = new { width = item.BeforeSize.Width, height = item.BeforeSize.Height },
                after = new { width = item.AfterSize.Width, height = item.AfterSize.Height },
                item.BaseEnergy, item.EffectiveEnergy, item.ProtectHits, item.PreferRemovalHits
            }),
            seamVsReference = BuildQuality(report.SeamVsReference),
            interpretation = "指标只描述 Seam 与普通缩放结果的算法间差异，不构成审美或语义质量排名。",
            report.Warnings
        };
        return JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
    }

    public byte[] SerializeCsv(SeamCarvingReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var builder = new StringBuilder();
        builder.Append("stepNumber,orientation,operation,beforeWidth,beforeHeight,afterWidth,afterHeight,baseEnergy,effectiveEnergy,protectHits,preferRemovalHits\r\n");
        foreach (var step in report.Steps.Take(SeamResourceEstimator.MaximumTotalSeams))
        {
            string[] values =
            [
                Format(step.StepNumber), step.Orientation.ToString(), step.Operation.ToString(),
                Format(step.BeforeSize.Width), Format(step.BeforeSize.Height), Format(step.AfterSize.Width),
                Format(step.AfterSize.Height), Format(step.BaseEnergy), Format(step.EffectiveEnergy),
                Format(step.ProtectHits), Format(step.PreferRemovalHits)
            ];
            builder.AppendJoin(',', values.Select(Escape)).Append("\r\n");
        }
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        var body = encoding.GetBytes(builder.ToString());
        var preamble = encoding.GetPreamble();
        var result = new byte[preamble.Length + body.Length];
        preamble.CopyTo(result, 0); body.CopyTo(result, preamble.Length);
        return result;
    }

    private static object? BuildQuality(FullReferenceQualityMetrics? quality) => quality is null ? null : new
    {
        quality.MeanAbsoluteErrorRgb,
        quality.RootMeanSquareErrorRgb,
        psnrRgb = new { isExact = double.IsPositiveInfinity(quality.PsnrRgbDb), valueDb = FiniteOrNull(quality.PsnrRgbDb) },
        psnrY = new { isExact = double.IsPositiveInfinity(quality.PsnrLumaDb), valueDb = FiniteOrNull(quality.PsnrLumaDb) },
        ssimY = quality.GlobalSsimLuma,
        quality.MaximumAbsoluteErrorRgb,
        quality.ChangedPixelCountRgb,
        alpha = new { quality.MeanAbsoluteErrorAlpha, quality.RootMeanSquareErrorAlpha,
            quality.MaximumAbsoluteErrorAlpha, quality.ChangedPixelCountAlpha }
    };

    private static double? FiniteOrNull(double value) => double.IsFinite(value) ? value : null;
    private static string Format<T>(T value) where T : IFormattable => value.ToString(null, CultureInfo.InvariantCulture);
    private static string Escape(string value) => value.IndexOfAny([',', '"', '\r', '\n']) < 0
        ? value : $"\"{value.Replace("\"", "\"\"")}\"";
}
