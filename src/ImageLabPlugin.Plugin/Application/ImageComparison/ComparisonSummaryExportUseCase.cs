using System.Text;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Infrastructure.Persistence;

namespace ImageLabPlugin.Application.ImageComparison;

internal sealed class ExportComparisonSummaryUseCase(
    ImageComparisonSummarySerializer serializer,
    IAtomicFileWriter writer) : IExportComparisonSummaryUseCase
{
    public async Task ExecuteAsync(ImageComparisonReport report, string targetPath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report); ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        var content = Encoding.UTF8.GetBytes(serializer.Serialize(report));
        await writer.WriteAsync(targetPath, content, cancellationToken).ConfigureAwait(false);
    }

    public string CreateJson(ImageComparisonReport report) => serializer.Serialize(report);
    public string CreateHumanReadableText(ImageComparisonReport report) => serializer.CreateHumanReadableText(report);
}
