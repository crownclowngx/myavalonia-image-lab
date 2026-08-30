using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;
using ImageLabPlugin.Application.Wavelets;

namespace ImageLabPlugin.Infrastructure.Persistence;

/// <summary>只负责 Wavelet 报告的 JSON/CSV 表达；不判断去噪优劣或水印结论。</summary>
internal sealed class WaveletExperimentReportSerializer : IWaveletReportSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public byte[] SerializeJson(WaveletExperimentReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return JsonSerializer.SerializeToUtf8Bytes(report, JsonOptions);
    }

    public byte[] SerializeCsv(WaveletExperimentReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var builder = new StringBuilder("section,sequence,carrier,case,levels,threshold,retainedRatio,residualRms,psnrLuma,ssimLuma,integrity,confidence,rawBer\r\n");
        foreach (var item in report.ScanCases)
        {
            builder.Append("scan,").Append(item.Sequence).Append(",,,").Append(item.Levels).Append(',')
                .Append(Format(item.Threshold)).Append(',').Append(Format(item.Statistics.RetainedRatio)).Append(',')
                .Append(Format(item.ResidualRms)).Append(',').Append(Format(item.PsnrLuma)).Append(',')
                .Append(Format(item.SsimLuma)).Append(",,,\r\n");
        }
        if (report.WatermarkBenchmark is not null)
        foreach (var item in report.WatermarkBenchmark.Cases)
        {
            builder.Append("watermark,,").Append(Escape(item.CarrierId)).Append(',').Append(Escape(item.CaseId))
                .Append(",,,,,,,").Append(item.IntegrityValid ? "true" : "false").Append(',')
                .Append(Format(item.Confidence)).Append(',').Append(Format(item.RawBitErrorRate)).Append("\r\n");
        }
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(builder.ToString());
    }

    private static string Format(double? value) => value is null ? string.Empty : value.Value.ToString("R", CultureInfo.InvariantCulture);
    private static string Escape(string value) => value.IndexOfAny([',', '"', '\r', '\n']) < 0
        ? value
        : $"\"{value.Replace("\"", "\"\"")}\"";
}
