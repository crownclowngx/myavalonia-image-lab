using System.Text;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Infrastructure.Fingerprinting;

namespace ImageLabPlugin.Application.Fingerprinting;

internal sealed class FingerprintReportExportUseCase(FingerprintReportSerializer serializer, IAtomicFileWriter writer) : IExportFingerprintReportUseCase
{
    public string CreateJson(FingerprintReport report) => serializer.Serialize(report);
    public string CreateHumanReadableText(FingerprintReport report) => serializer.CreateHumanReadableText(report);

    public Task ExecuteAsync(FingerprintReport report, string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return writer.WriteAsync(path, Encoding.UTF8.GetBytes(serializer.Serialize(report)), cancellationToken);
    }
}
