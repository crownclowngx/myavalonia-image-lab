using ImageLabPlugin.Domain.BitPlanes;
using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Application.BitPlanes;

/// <summary>一张已解码原图的 Document 私有会话。</summary>
/// <remarks>Dispose 主动切断最多约 64 MiB 的源像素引用；会话不能注册为 singleton 或跨 Document 共享。</remarks>
internal sealed class BitPlaneSession : IDisposable
{
    private bool _disposed;

    public BitPlaneSession(string sourcePath, PixelImage sourceImage)
    {
        SourcePath = sourcePath;
        SourceImage = sourceImage;
    }

    public string SourcePath { get; }
    public PixelImage SourceImage { get; private set; }
    public bool IsDisposed => _disposed;

    public void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BitPlaneSession));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SourceImage = new PixelImage(new ImageSize(1, 1), [0, 0, 0, 0]);
    }
}

/// <summary>当前通道的一份离散样本和一次扫描得到的八位统计。</summary>
internal sealed record BitPlaneChannelAnalysis(
    BitPlaneChannel Channel,
    BytePlane Plane,
    IReadOnlyList<BitPlaneStatistics> Statistics);

internal sealed record BitPlaneExportResult(string OutputPath, ImageSize Size, int ClippedPixelCount);

internal interface IPrepareBitPlaneSessionUseCase
{
    Task<BitPlaneSession> ExecuteAsync(string sourcePath, CancellationToken cancellationToken);
}

internal interface IAnalyzeBitPlaneChannelUseCase
{
    Task<BitPlaneChannelAnalysis> ExecuteAsync(
        BitPlaneSession session,
        BitPlaneChannel channel,
        CancellationToken cancellationToken);
}

internal interface IProjectBitPlaneViewUseCase
{
    Task<BitPlaneProjection> ExecuteAsync(
        BitPlaneSession session,
        BitPlaneChannelAnalysis analysis,
        BitMask8 mask,
        int focusedBit,
        CancellationToken cancellationToken);

    BitPlanePixelReport Inspect(
        BitPlaneSession session,
        BitPlaneChannelAnalysis analysis,
        BitMask8 mask,
        int sourceX,
        int sourceY);
}

internal interface IExportBitPlaneImageUseCase
{
    Task<BitPlaneExportResult> ExecuteAsync(
        BitPlaneSession session,
        BitPlaneChannelAnalysis analysis,
        BitMask8 mask,
        string outputPath,
        CancellationToken cancellationToken);
}
