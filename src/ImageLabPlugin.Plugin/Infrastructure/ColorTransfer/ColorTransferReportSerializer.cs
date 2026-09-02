using System.Globalization;
using System.Text;
using System.Text.Json;
using ImageLabPlugin.Application.ColorTransfer;
using ImageLabPlugin.Domain.ColorTransfer;
using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Infrastructure.ColorTransfer;

/// <summary>序列化版本化颜色实验报告，并严格阻止非有限数与隐私字段。</summary>
/// <remarks>
/// 报告模型不接收源路径、图片字节、用户名、机器名或异常堆栈。JSON 的 N/A 由 null+status 表达；
/// CSV 使用 UTF-8 BOM 和 RFC 风格双引号转义。任何 NaN/Infinity 都在写入前失败，不能静默变为 0。
/// </remarks>
internal sealed class ColorTransferReportSerializer : IColorTransferReportSerializer
{
    public byte[] Serialize(ColorExperimentReport report, ColorReportFormat format)
    {
        ArgumentNullException.ThrowIfNull(report); Validate(report);
        return format switch
        {
            ColorReportFormat.Json => WriteJson(report),
            ColorReportFormat.Csv => WriteCsv(report),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
    }

    private static byte[] WriteJson(ColorExperimentReport report)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", ColorTransferProtocols.ReportSchema);
            writer.WriteString("product", "Palette And Color Transfer／调色板与颜色迁移");
            writer.WriteString("colorProtocol", SrgbColorSpace.ProtocolId);
            writer.WriteString("alphaProtocol", ColorTransferProtocols.Alpha);
            writer.WriteString("clusteringProtocol", ColorTransferProtocols.Clustering);
            writer.WriteString("gamutMappingProtocol", ColorTransferProtocols.GamutMapping);
            writer.WriteString("operation", report.Operation.ToString());
            writer.WriteString("recipeFingerprint", report.RecipeFingerprint);
            WriteSize(writer, "targetSize", report.TargetSize);
            if (report.ReferenceSize is { } reference) WriteSize(writer, "referenceSize", reference); else writer.WriteNull("referenceSize");
            WriteStatistics(writer, "target", report.TargetStatistics);
            if (report.ReferenceStatistics is { } referenceStats) WriteStatistics(writer, "reference", referenceStats); else writer.WriteNull("reference");
            WriteStatistics(writer, "result", report.ResultStatistics);
            writer.WriteStartObject("difference");
            writer.WriteNumber("meanDeltaE00", report.Difference.Mean); writer.WriteNumber("p50DeltaE00", report.Difference.P50);
            writer.WriteNumber("p95DeltaE00", report.Difference.P95); writer.WriteNumber("maximumDeltaE00", report.Difference.Maximum);
            writer.WriteNumber("changedPixels", report.Difference.ChangedPixelCount); writer.WriteEndObject();
            writer.WriteStartObject("gamut"); writer.WriteNumber("unchanged", report.Gamut.UnchangedCount);
            writer.WriteNumber("chromaCompressed", report.Gamut.ChromaCompressedCount); writer.WriteNumber("lightnessClipped", report.Gamut.LightnessClippedCount);
            writer.WriteNumber("maximumDeltaE76", report.Gamut.MaximumDeltaE76); writer.WriteEndObject();
            writer.WriteStartObject("quality");
            WriteNullableFinite(writer, "psnrRgbDb", report.Quality.PsnrRgbDb);
            writer.WriteString("psnrRgbStatus", double.IsFinite(report.Quality.PsnrRgbDb) ? "available" : "identical-infinite");
            writer.WriteNumber("globalSsimLuma", report.Quality.GlobalSsimLuma);
            writer.WriteNumber("meanAbsoluteErrorRgb", report.Quality.MeanAbsoluteErrorRgb);
            writer.WriteNumber("rootMeanSquareErrorRgb", report.Quality.RootMeanSquareErrorRgb); writer.WriteEndObject();
            WriteCloseness(writer, "beforeReferenceCloseness", report.BeforeReferenceCloseness);
            WriteCloseness(writer, "afterReferenceCloseness", report.AfterReferenceCloseness);
            writer.WriteStartArray("palette");
            foreach (var entry in report.Palette?.Entries ?? [])
            {
                writer.WriteStartObject(); writer.WriteNumber("clusterIndex", entry.ClusterIndex);
                writer.WriteString("hex", Hex(entry.Srgb)); writer.WriteNumber("proportion", entry.Proportion);
                writer.WriteNumber("labL", entry.Lab.L); writer.WriteNumber("labA", entry.Lab.A);
                writer.WriteNumber("labB", entry.Lab.B); writer.WriteEndObject();
            }
            writer.WriteEndArray(); writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static byte[] WriteCsv(ColorExperimentReport report)
    {
        var builder = new StringBuilder("recordType,key,value,status\r\n");
        Row(builder, "protocol", "schema", ColorTransferProtocols.ReportSchema, "ok");
        Row(builder, "operation", "kind", report.Operation.ToString(), "ok");
        Row(builder, "metric", "meanDeltaE00", report.Difference.Mean.ToString("R", CultureInfo.InvariantCulture), "ok");
        Row(builder, "metric", "p95DeltaE00", report.Difference.P95.ToString("R", CultureInfo.InvariantCulture), "ok");
        Row(builder, "metric", "changedPixels", report.Difference.ChangedPixelCount.ToString(CultureInfo.InvariantCulture), "ok");
        Row(builder, "quality", "psnrRgbDb", double.IsFinite(report.Quality.PsnrRgbDb) ? report.Quality.PsnrRgbDb.ToString("R", CultureInfo.InvariantCulture) : string.Empty,
            double.IsFinite(report.Quality.PsnrRgbDb) ? "available" : "identical-infinite");
        Row(builder, "quality", "globalSsimLuma", report.Quality.GlobalSsimLuma.ToString("R", CultureInfo.InvariantCulture), "available");
        foreach (var entry in report.Palette?.Entries ?? [])
            Row(builder, "palette", $"cluster-{entry.ClusterIndex}",
                $"{Hex(entry.Srgb)}|{entry.Proportion.ToString("R", CultureInfo.InvariantCulture)}", "ok");
        var body = Encoding.UTF8.GetBytes(builder.ToString());
        return Encoding.UTF8.GetPreamble().Concat(body).ToArray();
    }

    private static void WriteStatistics(Utf8JsonWriter writer, string name, ColorStatistics value)
    {
        writer.WriteStartObject(name); writer.WriteNumber("pixelCount", value.PixelCount);
        writer.WriteNumber("visiblePixelCount", value.VisiblePixelCount); writer.WriteNumber("effectiveWeight", value.EffectiveWeight);
        writer.WriteNumber("meanL", value.MeanLab.L); writer.WriteNumber("meanA", value.MeanLab.A); writer.WriteNumber("meanB", value.MeanLab.B);
        writer.WriteNumber("stdL", value.StandardDeviationLab.L); writer.WriteNumber("stdA", value.StandardDeviationLab.A); writer.WriteNumber("stdB", value.StandardDeviationLab.B);
        if (value.CircularMeanHueDegrees is { } hue) writer.WriteNumber("circularMeanHueDegrees", hue); else writer.WriteNull("circularMeanHueDegrees");
        writer.WriteString("hueStatus", value.CircularMeanHueDegrees.HasValue ? "defined" : "not-applicable"); writer.WriteEndObject();
    }

    private static void WriteSize(Utf8JsonWriter writer, string name, ImageSize size)
    { writer.WriteStartObject(name); writer.WriteNumber("width", size.Width); writer.WriteNumber("height", size.Height); writer.WriteEndObject(); }
    private static void Row(StringBuilder builder, params string[] values) => builder.AppendJoin(',', values.Select(Escape)).Append("\r\n");
    private static string Escape(string value) => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    private static string Hex(SrgbColor color) { var bytes = color.ToBytes(); return $"#{bytes.Red:X2}{bytes.Green:X2}{bytes.Blue:X2}"; }
    private static void WriteNullableFinite(Utf8JsonWriter writer, string name, double value)
    { if (double.IsFinite(value)) writer.WriteNumber(name, value); else writer.WriteNull(name); }
    private static void WriteCloseness(Utf8JsonWriter writer, string name, DistributionCloseness? value)
    {
        if (value is null) { writer.WriteNull(name); return; }
        writer.WriteStartObject(name); writer.WriteNumber("meanResidual", value.MeanResidual);
        writer.WriteNumber("standardDeviationResidual", value.StandardDeviationResidual);
        writer.WriteNumber("jsdL", value.JensenShannonL); writer.WriteNumber("jsdA", value.JensenShannonA);
        writer.WriteNumber("jsdB", value.JensenShannonB); writer.WriteEndObject();
    }

    private static void Validate(ColorExperimentReport report)
    {
        var values = new[] { report.TargetStatistics.EffectiveWeight, report.TargetStatistics.MeanLab.L,
            report.TargetStatistics.MeanLab.A, report.TargetStatistics.MeanLab.B, report.ResultStatistics.EffectiveWeight,
            report.Difference.Mean, report.Difference.P50, report.Difference.P95, report.Difference.Maximum,
            report.Gamut.MaximumDeltaE76, report.Quality.GlobalSsimLuma, report.Quality.MeanAbsoluteErrorRgb,
            report.Quality.RootMeanSquareErrorRgb };
        if (values.Any(value => !double.IsFinite(value))) throw new InvalidDataException("报告包含 NaN 或 Infinity，已拒绝导出。");
    }
}
