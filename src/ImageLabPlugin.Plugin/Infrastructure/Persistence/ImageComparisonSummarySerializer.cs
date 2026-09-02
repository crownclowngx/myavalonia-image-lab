using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using ImageLabPlugin.Application.ImageComparison;
using ImageLabPlugin.Domain.ImageComparison;
using ImageLabPlugin.Domain.Shared.Analysis;
using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Infrastructure.Persistence;

/// <summary>以稳定属性顺序输出 schema 1 比较报告，并为非有限 PSNR 使用合法结构化 JSON。</summary>
internal sealed class ImageComparisonSummarySerializer
{
    public string Serialize(ImageComparisonReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", report.SchemaVersion);
            writer.WriteString("algorithmId", report.Summary.AlgorithmId);
            writer.WriteString("referenceName", Path.GetFileName(report.ReferenceName));
            writer.WriteString("candidateName", Path.GetFileName(report.CandidateName));
            writer.WriteString("completedAtUtc", report.CompletedAtUtc);
            WriteSize(writer, "referenceSize", report.Summary.ReferenceSize);
            WriteSize(writer, "candidateSize", report.Summary.CandidateSize);
            writer.WriteBoolean("isComparable", report.Summary.IsComparable);
            writer.WriteString("colorFormulaId", report.Summary.ColorFormulaId);
            writer.WriteString("alphaRule", report.Summary.AlphaRule);
            if (report.Summary.Mismatch is { } mismatch)
            {
                writer.WriteStartObject("mismatch");
                writer.WriteString("reason", mismatch.Reason.ToString());
                writer.WriteNumber("widthDifference", mismatch.WidthDifference);
                writer.WriteNumber("heightDifference", mismatch.HeightDifference);
                writer.WriteEndObject();
            }
            else writer.WriteNull("mismatch");
            WriteMetrics(writer, report.Summary.Metrics);
            WriteHistograms(writer, report.Summary.Histograms);
            if (report.Projection is { } projection)
            {
                writer.WriteStartObject("projection");
                writer.WriteString("kind", projection.Kind.ToString());
                writer.WriteNumber("amplification", projection.Amplification);
                if (projection.HeatmapSource is { } source) writer.WriteString("heatmapSource", source.ToString());
                else writer.WriteNull("heatmapSource");
                writer.WriteNumber("saturatedProxyPixelCount", projection.SaturatedProxyPixelCount);
                writer.WriteEndObject();
            }
            else writer.WriteNull("projection");
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public string CreateHumanReadableText(ImageComparisonReport report)
    {
        var summary = report.Summary;
        var title = $"{Path.GetFileName(report.ReferenceName)} ({summary.ReferenceSize.Width}×{summary.ReferenceSize.Height}) ↔ " +
                    $"{Path.GetFileName(report.CandidateName)} ({summary.CandidateSize.Width}×{summary.CandidateSize.Height})";
        if (!summary.IsComparable) return $"{title}\n未比较：{summary.Mismatch!.ToUserMessage()}\n算法：{summary.AlgorithmId}";
        var metrics = summary.Metrics!;
        var alpha = metrics.ChangedPixelCountAlpha == 0
            ? "Alpha 未变化"
            : $"Alpha 变化 {metrics.ChangedPixelCountAlpha:N0} 像素（{metrics.ChangedPixelRatioAlpha:P4}）";
        return $"{title}\nPSNR-Y：{FormatPsnr(metrics.PsnrLumaDb)}\nPSNR-RGB：{FormatPsnr(metrics.PsnrRgbDb)}\n" +
               $"全局 SSIM-Y：{metrics.GlobalSsimLuma:F8}\nRGB MAE：{metrics.MeanAbsoluteErrorRgb:F6}；" +
               $"RMSE：{metrics.RootMeanSquareErrorRgb:F6}；最大差异：{metrics.MaximumAbsoluteErrorRgb}\n" +
               $"RGB 变化像素：{metrics.ChangedPixelCountRgb:N0}（{metrics.ChangedPixelRatioRgb:P4}）；{alpha}\n算法：{summary.AlgorithmId}";
    }

    private static string FormatPsnr(double value) => double.IsPositiveInfinity(value) ? "∞（像素误差为 0）" : $"{value:F6} dB";

    private static void WriteSize(Utf8JsonWriter writer, string propertyName, ImageSize size)
    {
        writer.WriteStartObject(propertyName); writer.WriteNumber("width", size.Width); writer.WriteNumber("height", size.Height); writer.WriteEndObject();
    }

    private static void WriteMetrics(Utf8JsonWriter writer, FullReferenceQualityMetrics? metrics)
    {
        if (metrics is null) { writer.WriteNull("metrics"); return; }
        writer.WriteStartObject("metrics");
        WriteFinite(writer, "psnrLumaDb", metrics.PsnrLumaDb); WriteFinite(writer, "psnrRgbDb", metrics.PsnrRgbDb);
        writer.WriteNumber("globalSsimLuma", metrics.GlobalSsimLuma);
        writer.WriteNumber("meanSquaredErrorLuma", metrics.MeanSquaredErrorLuma);
        writer.WriteNumber("meanSquaredErrorRgb", metrics.MeanSquaredErrorRgb);
        writer.WriteNumber("meanAbsoluteErrorRgb", metrics.MeanAbsoluteErrorRgb);
        writer.WriteNumber("rootMeanSquareErrorRgb", metrics.RootMeanSquareErrorRgb);
        writer.WriteNumber("maximumAbsoluteErrorRgb", metrics.MaximumAbsoluteErrorRgb);
        writer.WriteNumber("changedPixelCountRgb", metrics.ChangedPixelCountRgb);
        writer.WriteNumber("changedPixelRatioRgb", metrics.ChangedPixelRatioRgb);
        writer.WriteNumber("meanAbsoluteErrorAlpha", metrics.MeanAbsoluteErrorAlpha);
        writer.WriteNumber("rootMeanSquareErrorAlpha", metrics.RootMeanSquareErrorAlpha);
        writer.WriteNumber("maximumAbsoluteErrorAlpha", metrics.MaximumAbsoluteErrorAlpha);
        writer.WriteNumber("changedPixelCountAlpha", metrics.ChangedPixelCountAlpha);
        writer.WriteNumber("changedPixelRatioAlpha", metrics.ChangedPixelRatioAlpha);
        writer.WriteEndObject();
    }

    private static void WriteFinite(Utf8JsonWriter writer, string name, double value)
    {
        writer.WriteStartObject(name);
        if (double.IsFinite(value)) writer.WriteNumber("value", value); else writer.WriteNull("value");
        writer.WriteBoolean("isInfinite", double.IsPositiveInfinity(value));
        writer.WriteEndObject();
    }

    private static void WriteHistograms(Utf8JsonWriter writer, ImagePairHistograms? histograms)
    {
        if (histograms is null) { writer.WriteNull("histograms"); return; }
        writer.WriteStartObject("histograms");
        foreach (var (name, channel) in new[] { ("r", ImageChannel.Red), ("g", ImageChannel.Green), ("b", ImageChannel.Blue), ("y", ImageChannel.Luma), ("cb", ImageChannel.ChromaBlue), ("cr", ImageChannel.ChromaRed) })
        {
            writer.WriteStartObject(name); WriteBins(writer, "reference", histograms.Reference.GetBins(channel));
            WriteBins(writer, "candidate", histograms.Candidate.GetBins(channel)); writer.WriteEndObject();
        }
        writer.WriteEndObject();
    }

    private static void WriteBins(Utf8JsonWriter writer, string name, IReadOnlyList<long> bins)
    {
        writer.WriteStartArray(name); foreach (var value in bins) writer.WriteNumberValue(value); writer.WriteEndArray();
    }
}
