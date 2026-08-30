using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.BitPlanes;

namespace ImageLabPlugin.Application.BitPlanes;

/// <summary>只负责解码一次并建立会话，不分析任何通道。</summary>
internal sealed class PrepareBitPlaneSessionUseCase(IImageCodec codec) : IPrepareBitPlaneSessionUseCase
{
    public async Task<BitPlaneSession> ExecuteAsync(string sourcePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var source = await codec.DecodeAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        return new BitPlaneSession(sourcePath, source);
    }
}

/// <summary>抽取一个通道并在同一次应用操作中生成全部八位统计。</summary>
internal sealed class AnalyzeBitPlaneChannelUseCase(
    BitPlaneChannelExtractor extractor,
    BitPlaneStatisticsAnalyzer analyzer) : IAnalyzeBitPlaneChannelUseCase
{
    public Task<BitPlaneChannelAnalysis> ExecuteAsync(
        BitPlaneSession session,
        BitPlaneChannel channel,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.ThrowIfDisposed();
        return Task.Run(() =>
        {
            var plane = extractor.Extract(session.SourceImage, channel, cancellationToken);
            var statistics = analyzer.Analyze(plane, cancellationToken);
            return new BitPlaneChannelAnalysis(channel, plane, statistics);
        }, cancellationToken);
    }
}

/// <summary>协调有界投影与 O(1) 像素探针，不执行文件或 Bitmap 操作。</summary>
internal sealed class ProjectBitPlaneViewUseCase(
    BitPlaneProjector projector,
    BitPlanePixelInspector inspector) : IProjectBitPlaneViewUseCase
{
    public Task<BitPlaneProjection> ExecuteAsync(
        BitPlaneSession session,
        BitPlaneChannelAnalysis analysis,
        BitMask8 mask,
        int focusedBit,
        CancellationToken cancellationToken)
    {
        session.ThrowIfDisposed();
        return Task.Run(() => projector.Project(
            session.SourceImage, analysis.Plane, mask, focusedBit, 1024, cancellationToken), cancellationToken);
    }

    public BitPlanePixelReport Inspect(
        BitPlaneSession session,
        BitPlaneChannelAnalysis analysis,
        BitMask8 mask,
        int sourceX,
        int sourceY)
    {
        session.ThrowIfDisposed();
        return inspector.Inspect(session.SourceImage, analysis.Plane, mask, sourceX, sourceY);
    }
}

/// <summary>按需创建完整尺寸重建、编码 PNG 并通过原子写入端口发布。</summary>
/// <remarks>
/// 完整结果和 PNG 字节都只活在该方法内，写入结束后即可被回收；Document 不缓存第二张完整图片。
/// V1 的接口固定 PNG，故调用方无法误选 JPEG 并破坏低位事实。
/// </remarks>
internal sealed class ExportBitPlaneImageUseCase(
    BitPlaneReconstructor reconstructor,
    IImageCodec codec,
    IAtomicFileWriter writer) : IExportBitPlaneImageUseCase
{
    public async Task<BitPlaneExportResult> ExecuteAsync(
        BitPlaneSession session,
        BitPlaneChannelAnalysis analysis,
        BitMask8 mask,
        string outputPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        session.ThrowIfDisposed();
        var reconstructed = await Task.Run(() => reconstructor.Reconstruct(
            session.SourceImage, analysis.Plane, mask, cancellationToken), cancellationToken).ConfigureAwait(false);
        var bytes = await codec.EncodeAsync(reconstructed.Image, ImageOutputFormat.Png, 100, cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync(outputPath, bytes, cancellationToken).ConfigureAwait(false);
        return new BitPlaneExportResult(outputPath, reconstructed.Image.Size, reconstructed.ClippedPixelCount);
    }
}
