using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ImageLabPlugin.Domain.Robustness;

namespace ImageLabPlugin.Infrastructure.Robustness;

/// <summary>只序列化已完成的稳定报告；不重新执行实验，也不接触密码、Payload、绝对路径或 Mapping Key。</summary>
internal sealed class RobustnessReportSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public string SerializeJson(RobustnessExperimentReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return JsonSerializer.Serialize(report with { SourceName = Path.GetFileName(report.SourceName) }, Options);
    }

    public string SerializeCsv(RobustnessExperimentReport report)
    {
        ArgumentNullException.ThrowIfNull(report); var builder = new StringBuilder();
        builder.AppendLine("schema_version,recipe_hash,profile,scan_point_index,scan_value,trial_index,completed,success,failure_reason,first_failure_step,recovered_after_failure,header_physical_ber,header_voted_ber,data_physical_ber,data_voted_ber,header_rs,data_rs,confidence,attack_psnr_y,end_to_end_psnr_y,operator_error,ahash_distance,dhash_distance,phash_distance");
        foreach (var item in report.Cases.OrderBy(value => value.Key.Profile).ThenBy(value => value.Key.ScanPointIndex).ThenBy(value => value.Key.TrialIndex))
        {
            var d = item.FinalDiagnostic;
            Append(builder, report.SchemaVersion, report.RecipeHash, item.Key.Profile, item.Key.ScanPointIndex, item.Key.CanonicalValue, item.Key.TrialIndex, item.Completed, d?.Success,
                d?.FailureReason, item.FirstObservedUnrecoverableStep, item.RecoveredAfterFailure, d?.Header?.PhysicalRawBer.Ratio, d?.Header?.VotedPreEccBer.Ratio,
                d?.Data?.PhysicalRawBer.Ratio, d?.Data?.VotedPreEccBer.Ratio, d?.Header?.CorrectedSymbols, d?.Data?.CorrectedSymbols,
                d?.Data?.MeanConfidence ?? d?.Header?.MeanConfidence, item.AttackOnlyQuality.Metrics?.PsnrLumaDb, item.EndToEndQuality.Metrics?.PsnrLumaDb, item.OperatorError,
                Distance(item, "ahash-8x8-mean64-luma-v1"), Distance(item, "dhash-horizontal-9x8-64-luma-v1"), Distance(item, "phash-dct32-low8-median64-luma-v1"));
        }
        return builder.ToString();
    }

    private static int? Distance(RobustnessCaseResult item, string algorithmId) => item.FingerprintObservations?
        .FirstOrDefault(value => value.AlgorithmId.Value == algorithmId)?.Distance.Distance;

    private static void Append(StringBuilder builder, params object?[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            if (i > 0) builder.Append(','); var text = values[i] switch
            {
                null => string.Empty,
                double value when double.IsPositiveInfinity(value) => "Infinity",
                IFormattable value => value.ToString(null, CultureInfo.InvariantCulture),
                _ => values[i]!.ToString() ?? string.Empty
            };
            if (text.IndexOfAny([',', '"', '\r', '\n']) >= 0) builder.Append('"').Append(text.Replace("\"", "\"\"")).Append('"'); else builder.Append(text);
        }
        builder.AppendLine();
    }
}
