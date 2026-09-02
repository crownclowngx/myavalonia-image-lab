using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Application.MagnitudePhaseSwap;

/// <summary>编码、内存回读、原子发布并从真实目标再次回读当前规范画布 PNG。</summary>
internal sealed class ExportMagnitudePhaseImageUseCase(IImageCodec codec, IAtomicFileWriter writer)
    : IExportMagnitudePhaseImageUseCase
{
    public async Task ExecuteAsync(MagnitudePhaseSession session, MagnitudePhaseRenderResult result,
        string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(path); session.ThrowIfDisposed();
        if (!ReferenceEquals(session.CurrentResult, result)) throw new InvalidOperationException("只能导出当前已提交结果。");
        MagnitudePhaseExportPathGuard.EnsureNotInput(session, path);
        var bytes = await codec.EncodeAsync(result.ResultImage, ImageOutputFormat.Png, 100, cancellationToken).ConfigureAwait(false);
        var memoryReadback = await codec.DecodeAsync(bytes, cancellationToken).ConfigureAwait(false);
        Verify(memoryReadback, result.ResultImage);
        await writer.WriteAsync(path, bytes, cancellationToken).ConfigureAwait(false);
        var targetReadback = await codec.DecodeAsync(path, cancellationToken).ConfigureAwait(false);
        Verify(targetReadback, result.ResultImage);
    }

    private static void Verify(PixelImage actual, PixelImage expected)
    {
        if (actual.Size != expected.Size || !actual.Rgba.Span.SequenceEqual(expected.Rgba.Span))
            throw new InvalidDataException("PNG 回读与当前结果的尺寸或 RGBA 内容不一致。");
    }

}

internal sealed class ImportMagnitudePhaseRecipeUseCase(ITextFileReader reader,
    IMagnitudePhaseRecipeSerializer serializer) : IImportMagnitudePhaseRecipeUseCase
{
    public async Task<(Domain.MagnitudePhaseSwap.MagnitudePhaseRecipe Recipe, string FingerprintA, string FingerprintB)>
        ExecuteAsync(string path, CancellationToken cancellationToken)
    {
        var bytes = await reader.ReadAsync(path, MagnitudePhaseProtocol.MaximumJsonBytes, cancellationToken).ConfigureAwait(false);
        var recipe = serializer.Deserialize(bytes, out var a, out var b);
        return (recipe, a, b);
    }
}

internal sealed class ExportMagnitudePhaseRecipeUseCase(IMagnitudePhaseRecipeSerializer serializer,
    IAtomicFileWriter writer) : IExportMagnitudePhaseRecipeUseCase
{
    public Task ExecuteAsync(Domain.MagnitudePhaseSwap.MagnitudePhaseRecipe recipe,
        MagnitudePhaseSession session, string path, CancellationToken cancellationToken)
    {
        MagnitudePhaseExportPathGuard.EnsureNotInput(session, path);
        return writer.WriteAsync(path, serializer.Serialize(recipe, session.FingerprintA, session.FingerprintB), cancellationToken);
    }
}

internal sealed class ExportMagnitudePhaseReportUseCase(IMagnitudePhaseReportSerializer serializer,
    IAtomicFileWriter writer) : IExportMagnitudePhaseReportUseCase
{
    public Task ExecuteAsync(MagnitudePhaseReport report, MagnitudePhaseSession session, string path, bool csv,
        CancellationToken cancellationToken)
    {
        MagnitudePhaseExportPathGuard.EnsureNotInput(session, path);
        return writer.WriteAsync(path, csv ? serializer.SerializeCsv(report) : serializer.SerializeJson(report), cancellationToken);
    }
}

internal static class MagnitudePhaseExportPathGuard
{
    public static void EnsureNotInput(MagnitudePhaseSession session, string path)
    {
        ArgumentNullException.ThrowIfNull(session); ArgumentException.ThrowIfNullOrWhiteSpace(path); session.ThrowIfDisposed();
        if (SamePath(path, session.PathA) || SamePath(path, session.PathB))
            throw new InvalidOperationException("导出目标不得覆盖输入 A 或 B。");
    }

    private static bool SamePath(string first, string second)
    {
        try { return StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(first), Path.GetFullPath(second)); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        { throw new InvalidOperationException("无法规范化导出或输入路径。", exception); }
    }
}
