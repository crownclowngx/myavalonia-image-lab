using System.Globalization;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.ColorTransfer;
using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Application.ColorTransfer;

internal sealed class PrepareColorTransferSessionUseCase(IImageCodec codec, ImageAnalysisProxyProjector projector)
    : IPrepareColorTransferSessionUseCase
{
    public async Task<PreparedColorImage> ExecuteAsync(string path, int previewMaximumEdge, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (previewMaximumEdge != 512) throw new ArgumentOutOfRangeException(nameof(previewMaximumEdge));
        var image = await codec.DecodeAsync(path, cancellationToken).ConfigureAwait(false);
        var preview = projector.Create(image, previewMaximumEdge, cancellationToken);
        return new PreparedColorImage(path, image, preview, ContentFingerprint(image));
    }

    internal static string ContentFingerprint(PixelImage image)
    {
        ulong hash = 14695981039346656037UL;
        foreach (var value in image.Rgba.Span) { hash ^= value; hash *= 1099511628211UL; }
        hash ^= (uint)image.Size.Width; hash *= 1099511628211UL; hash ^= (uint)image.Size.Height;
        return hash.ToString("x16", CultureInfo.InvariantCulture);
    }
}

internal sealed class AnalyzeColorDistributionsUseCase(ColorDistributionAnalyzer analyzer,
    RgbColorAggregator aggregator, DominantColorClusterer clusterer) : IAnalyzeColorDistributionsUseCase
{
    public Task<ColorAnalysisResult> ExecuteAsync(PixelImage image, int colorCount, PaletteSource source,
        CancellationToken cancellationToken) => Task.Run(() =>
    {
        var distribution = analyzer.Analyze(image, cancellationToken);
        var aggregated = aggregator.Aggregate(image, cancellationToken);
        return new ColorAnalysisResult(distribution, clusterer.Cluster(aggregated, colorCount, source, cancellationToken));
    }, cancellationToken);
}

internal sealed class FreezePaletteUseCase : IFreezePaletteUseCase
{
    public FrozenPalette Execute(ExtractedPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        if (!palette.Converged) throw new InvalidOperationException("聚类未收敛，不能冻结为可执行调色板。");
        if (palette.Entries.Count is < 2 or > 12) throw new InvalidOperationException("冻结调色板必须包含 2–12 个有效颜色。");
        var entries = palette.Entries.OrderBy(item => item.ClusterIndex).ToArray();
        return new FrozenPalette(palette.Fingerprint, Array.AsReadOnly(entries), palette.Source,
            $"frozen:{palette.Source}:{DominantColorClusterer.Fingerprint(entries)}");
    }
}

internal sealed class RunColorTransferUseCase(LabStatisticsTransfer transfer) : IRunColorTransferUseCase
{
    public Task<ColorOperationResult> ExecuteAsync(PixelImage target, ColorDistributionSnapshot targetDistribution,
        ColorDistributionSnapshot referenceDistribution, ColorTransferRecipe recipe, CancellationToken cancellationToken) =>
        Task.Run(() => transfer.Transfer(target, targetDistribution, referenceDistribution, recipe, cancellationToken), cancellationToken);
}

internal sealed class RemapToPaletteUseCase(FixedPaletteRemapper remapper) : IRemapToPaletteUseCase
{
    public Task<ColorOperationResult> ExecuteAsync(PixelImage target, FrozenPalette palette, CancellationToken cancellationToken) =>
        Task.Run(() => remapper.Remap(target, palette, cancellationToken), cancellationToken);
}

internal sealed class ExportColorResultUseCase(IImageCodec codec, IAtomicFileWriter writer) : IExportColorResultUseCase
{
    public async Task ExecuteAsync(PixelImage result, string outputPath, string targetPath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result); ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (Path.GetFullPath(outputPath).Equals(Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("结果不能覆盖目标输入图片。");
        var encoded = await codec.EncodeAsync(result, ImageOutputFormat.Png, 100, cancellationToken).ConfigureAwait(false);
        var decoded = await codec.DecodeAsync(encoded, cancellationToken).ConfigureAwait(false);
        if (decoded.Size != result.Size || !IsRoundTripEquivalent(decoded, result))
            throw new InvalidDataException("PNG 编码回读的尺寸或 RGBA 与结果不一致，已阻止发布。");
        await writer.WriteAsync(outputPath, encoded, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsRoundTripEquivalent(PixelImage decoded, PixelImage source)
    {
        var left = decoded.Rgba.Span; var right = source.Rgba.Span;
        for (var i = 0; i < left.Length; i += 4)
        {
            if (left[i + 3] != right[i + 3]) return false;
            // Avalonia/Skia 的半透明像素会经历预乘/反预乘，允许既有 codec 测试冻结的 3 字节舍入误差。
            // A=0 的隐藏 RGB 无法通过平台 Bitmap 往返，但领域结果在导出前仍逐字节保留该事实。
            if (right[i + 3] == 0) continue;
            if (Math.Abs(left[i] - right[i]) > 3 || Math.Abs(left[i + 1] - right[i + 1]) > 3 || Math.Abs(left[i + 2] - right[i + 2]) > 3) return false;
        }
        return true;
    }
}

internal sealed class ExportColorReportUseCase(IColorTransferReportSerializer serializer, IAtomicFileWriter writer)
    : IExportColorReportUseCase
{
    public Task ExecuteAsync(ColorExperimentReport report, ColorReportFormat format, string outputPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report); ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        return writer.WriteAsync(outputPath, serializer.Serialize(report, format), cancellationToken);
    }
}
