using System.Text;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.Robustness;
using ImageLabPlugin.Infrastructure.Robustness;

namespace ImageLabPlugin.Application.Robustness;

internal sealed class RobustnessReportExportUseCase(RobustnessReportSerializer serializer, IAtomicFileWriter writer) : IExportRobustnessReportUseCase
{
    public Task ExportJsonAsync(RobustnessExperimentReport report, string path, CancellationToken token) => WriteAsync(serializer.SerializeJson(report), path, token);
    public Task ExportCsvAsync(RobustnessExperimentReport report, string path, CancellationToken token) => WriteAsync(serializer.SerializeCsv(report), path, token);
    public string CreateJson(RobustnessExperimentReport report) => serializer.SerializeJson(report);
    public string CreateCsv(RobustnessExperimentReport report) => serializer.SerializeCsv(report);
    private Task WriteAsync(string content, string path, CancellationToken token)
    { ArgumentException.ThrowIfNullOrWhiteSpace(path); return writer.WriteAsync(path, Encoding.UTF8.GetBytes(content), token); }
}
