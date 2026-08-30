using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ImageLabPlugin.Application.Fingerprinting;

namespace ImageLabPlugin.Infrastructure.Fingerprinting;

/// <summary>以稳定字段顺序输出 schema 1；只写文件名和算法事实，不泄露绝对路径、像素或异常堆栈。</summary>
internal sealed class FingerprintReportSerializer
{
    public string Serialize(FingerprintReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            var summary = report.Comparison;
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", report.SchemaVersion);
            writer.WriteString("completedAtUtc", summary.CompletedAtUtc);
            writer.WriteString("referenceName", Path.GetFileName(summary.Reference.Name));
            writer.WriteString("candidateName", Path.GetFileName(summary.Candidate.Name));
            WriteImage(writer, "reference", summary.Reference);
            WriteImage(writer, "candidate", summary.Candidate);
            writer.WriteString("normalizationId", summary.NormalizationId);
            writer.WriteString("decisionPolicyId", summary.DecisionPolicyId);
            writer.WriteString("overview", summary.Overview.ToString());
            writer.WriteString("disclaimer", summary.Disclaimer);
            writer.WriteStartArray("algorithms");
            foreach (var item in summary.Algorithms)
            {
                writer.WriteStartObject();
                writer.WriteString("algorithmId", item.AlgorithmId.Value);
                writer.WriteString("referenceFingerprint", item.Reference.ToCanonicalHex());
                writer.WriteString("candidateFingerprint", item.Candidate.ToCanonicalHex());
                writer.WriteNumber("distance", item.Distance.Distance);
                writer.WriteNumber("bitSimilarityPercent", Math.Round(item.Distance.BitSimilarityPercent, 2));
                writer.WriteNumber("referenceThreshold", item.ReferenceThreshold);
                writer.WriteString("decision", item.Decision.ToString());
                writer.WriteNumber("elapsedMilliseconds", item.Elapsed.TotalMilliseconds);
                writer.WriteString("limitation", item.Limitation);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            WriteStability(writer, report.Stability);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public string CreateHumanReadableText(FingerprintReport report)
    {
        var summary = report.Comparison;
        var builder = new StringBuilder()
            .Append(Path.GetFileName(summary.Reference.Name)).Append(" ↔ ").AppendLine(Path.GetFileName(summary.Candidate.Name))
            .Append("总览：").AppendLine(ToChinese(summary.Overview));
        foreach (var item in summary.Algorithms)
            builder.Append(item.AlgorithmId.Value).Append("：").Append(item.Reference.ToCanonicalHex()).Append(" ↔ ")
                .Append(item.Candidate.ToCanonicalHex()).Append("；距离 ").Append(item.Distance.Distance).Append("/64；位相似度 ")
                .Append(item.Distance.BitSimilarityPercent.ToString("F2")).Append("%；").AppendLine(ToChinese(item.Decision));
        return builder.Append(summary.Disclaimer).ToString();
    }

    private static void WriteImage(Utf8JsonWriter writer, string name, FingerprintImageFacts facts)
    {
        writer.WriteStartObject(name);
        writer.WriteNumber("width", facts.Size.Width);
        writer.WriteNumber("height", facts.Size.Height);
        writer.WriteBoolean("hasAlpha", facts.HasAlpha);
        writer.WriteEndObject();
    }

    private static void WriteStability(Utf8JsonWriter writer, FingerprintStabilityResult? result)
    {
        if (result is null) { writer.WriteNull("stability"); return; }
        writer.WriteStartObject("stability");
        writer.WriteString("kind", result.Recipe.Kind.ToString());
        writer.WriteBoolean("isComplete", result.IsComplete);
        writer.WriteString("notice", result.Notice);
        writer.WriteStartArray("points");
        foreach (var point in result.Points)
        {
            writer.WriteStartObject();
            writer.WriteNumber("requestedValue", point.RequestedValue);
            writer.WriteNumber("width", point.OutputSize.Width);
            writer.WriteNumber("height", point.OutputSize.Height);
            if (point.JpegEncodedBytes is long bytes) writer.WriteNumber("jpegEncodedBytes", bytes); else writer.WriteNull("jpegEncodedBytes");
            if (point.Error is null) writer.WriteNull("error"); else writer.WriteString("error", point.Error);
            writer.WriteStartArray("algorithms");
            foreach (var item in point.Algorithms)
            {
                writer.WriteStartObject(); writer.WriteString("algorithmId", item.AlgorithmId.Value);
                writer.WriteString("fingerprint", item.Fingerprint.ToCanonicalHex()); writer.WriteNumber("distance", item.Distance.Distance);
                writer.WriteNumber("bitSimilarityPercent", Math.Round(item.Distance.BitSimilarityPercent, 2)); writer.WriteString("decision", item.Decision.ToString()); writer.WriteEndObject();
            }
            writer.WriteEndArray(); writer.WriteEndObject();
        }
        writer.WriteEndArray(); writer.WriteEndObject();
    }

    private static string ToChinese(Domain.Fingerprinting.FingerprintOverview value) => value switch
    {
        Domain.Fingerprinting.FingerprintOverview.ConsistentlyNear => "一致接近",
        Domain.Fingerprinting.FingerprintOverview.ConsistentlyNotNear => "一致不接近",
        Domain.Fingerprinting.FingerprintOverview.Incomplete => "结果不完整",
        _ => "结果分歧，需要人工复核"
    };

    private static string ToChinese(Domain.Fingerprinting.FingerprintDecision value) => value switch
    {
        Domain.Fingerprinting.FingerprintDecision.ExactFingerprintMatch => "摘要完全相同",
        Domain.Fingerprinting.FingerprintDecision.NearUnderReferencePolicy => "参考策略下接近",
        Domain.Fingerprinting.FingerprintDecision.NotNearUnderReferencePolicy => "参考策略下不接近",
        _ => "不可比较"
    };
}
