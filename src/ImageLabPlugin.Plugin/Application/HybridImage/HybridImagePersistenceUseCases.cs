using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.HybridImage;

namespace ImageLabPlugin.Application.HybridImage;

/// <summary>只发布当前 Session 的完整尺寸 PNG，并执行内存与真实目标两次无损事实回读。</summary>
internal sealed class ExportHybridImageUseCase(IImageCodec codec, IAtomicFileWriter writer) : IExportHybridImageUseCase
{
    public async Task ExecuteAsync(HybridImageSession session, HybridRenderResult result,
        HybridImageRecipe recipe, string outputPath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        session.ThrowIfDisposed();
        if (!result.IsFullSize || !ReferenceEquals(session.LastFullSize, result) ||
            !StringComparer.Ordinal.Equals(result.SessionFingerprint, session.SessionFingerprint) ||
            !StringComparer.Ordinal.Equals(result.RecipeFingerprint, recipe.Fingerprint()))
            throw new InvalidOperationException("只有当前输入与配方对应的完整尺寸结果可以导出。");
        var target = Path.GetFullPath(outputPath);
        if (target.Equals(Path.GetFullPath(session.PathA), StringComparison.OrdinalIgnoreCase) ||
            target.Equals(Path.GetFullPath(session.PathB), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Hybrid Image 不允许覆盖图像 A 或 B。");

        var bytes = await codec.EncodeAsync(result.Composition.Quantized, ImageOutputFormat.Png, 100,
            cancellationToken).ConfigureAwait(false);
        var memoryReadback = await codec.DecodeAsync(bytes, cancellationToken).ConfigureAwait(false);
        Verify(memoryReadback, result.Composition.Quantized, "内存");
        await writer.WriteAsync(target, bytes, cancellationToken).ConfigureAwait(false);
        var targetReadback = await codec.DecodeAsync(target, cancellationToken).ConfigureAwait(false);
        Verify(targetReadback, result.Composition.Quantized, "目标文件");
    }

    private static void Verify(Domain.Imaging.PixelImage actual, Domain.Imaging.PixelImage expected, string stage)
    {
        if (actual.Size != expected.Size || !actual.Rgba.Span.SequenceEqual(expected.Rgba.Span))
            throw new InvalidOperationException($"PNG {stage}回读与完整尺寸结果不一致。");
        for (var i = 0; i < actual.Rgba.Length; i += 4)
        {
            var pixels = actual.Rgba.Span;
            if (pixels[i] != pixels[i + 1] || pixels[i] != pixels[i + 2] || pixels[i + 3] != 255)
                throw new InvalidOperationException($"PNG {stage}回读不是不透明灰度 RGBA。");
        }
    }
}

internal sealed class ImportHybridRecipeUseCase(ITextFileReader reader, IHybridImageRecipeSerializer serializer)
    : IImportHybridRecipeUseCase
{
    public async Task<(HybridImageRecipe Recipe, string FingerprintA, string FingerprintB)> ExecuteAsync(
        string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var bytes = await reader.ReadAsync(path, HybridImageProtocol.MaximumJsonBytes, cancellationToken)
            .ConfigureAwait(false);
        var recipe = serializer.Deserialize(bytes, out var fingerprintA, out var fingerprintB);
        return (recipe, fingerprintA, fingerprintB);
    }
}

internal sealed class ExportHybridRecipeUseCase(IHybridImageRecipeSerializer serializer, IAtomicFileWriter writer)
    : IExportHybridRecipeUseCase
{
    public Task ExecuteAsync(HybridImageRecipe recipe, HybridImageSession session, string path,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(session);
        session.ThrowIfDisposed();
        return writer.WriteAsync(path, serializer.Serialize(recipe, session.FingerprintA, session.FingerprintB),
            cancellationToken);
    }
}

internal sealed class ExportHybridReportUseCase(IHybridImageReportSerializer serializer, IAtomicFileWriter writer)
    : IExportHybridReportUseCase
{
    public Task ExecuteAsync(HybridImageReport report, string path, bool csv, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var bytes = csv ? serializer.SerializeCsv(report) : serializer.SerializeJson(report);
        return writer.WriteAsync(path, bytes, cancellationToken);
    }
}
