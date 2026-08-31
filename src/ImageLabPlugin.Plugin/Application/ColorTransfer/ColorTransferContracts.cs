using ImageLabPlugin.Domain.ColorTransfer;
using ImageLabPlugin.Domain.Imaging;
using ImageLabPlugin.Domain.Comparison;

namespace ImageLabPlugin.Application.ColorTransfer;

internal sealed record PreparedColorImage(string Path, PixelImage FullImage, PixelImage Preview, string ContentFingerprint);
internal sealed record ColorAnalysisResult(ColorDistributionSnapshot Distribution, ExtractedPalette Palette);

internal interface IPrepareColorTransferSessionUseCase
{
    Task<PreparedColorImage> ExecuteAsync(string path, int previewMaximumEdge, CancellationToken cancellationToken);
}

internal interface IAnalyzeColorDistributionsUseCase
{
    Task<ColorAnalysisResult> ExecuteAsync(PixelImage image, int colorCount, PaletteSource source, CancellationToken cancellationToken);
}

internal interface IFreezePaletteUseCase
{
    FrozenPalette Execute(ExtractedPalette palette);
}

internal interface IRunColorTransferUseCase
{
    Task<ColorOperationResult> ExecuteAsync(PixelImage target, ColorDistributionSnapshot targetDistribution,
        ColorDistributionSnapshot referenceDistribution, ColorTransferRecipe recipe, CancellationToken cancellationToken);
}

internal interface IRemapToPaletteUseCase
{
    Task<ColorOperationResult> ExecuteAsync(PixelImage target, FrozenPalette palette, CancellationToken cancellationToken);
}

internal interface IExportColorResultUseCase
{
    Task ExecuteAsync(PixelImage result, string outputPath, string targetPath, CancellationToken cancellationToken);
}

internal enum ColorReportFormat { Json, Csv }

internal sealed record ColorExperimentReport(ColorOperationKind Operation, string RecipeFingerprint,
    ImageSize TargetSize, ImageSize? ReferenceSize, ColorStatistics TargetStatistics,
    ColorStatistics? ReferenceStatistics, ColorStatistics ResultStatistics, FrozenPalette? Palette,
    DifferenceSummary Difference, GamutMappingDiagnostics Gamut, FullReferenceQualityMetrics Quality,
    DistributionCloseness? BeforeReferenceCloseness, DistributionCloseness? AfterReferenceCloseness);

internal interface IColorTransferReportSerializer
{
    byte[] Serialize(ColorExperimentReport report, ColorReportFormat format);
}

internal interface IExportColorReportUseCase
{
    Task ExecuteAsync(ColorExperimentReport report, ColorReportFormat format, string outputPath, CancellationToken cancellationToken);
}
