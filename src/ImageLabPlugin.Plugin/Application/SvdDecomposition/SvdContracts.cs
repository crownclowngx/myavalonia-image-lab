using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Domain.SvdDecomposition;

namespace ImageLabPlugin.Application.SvdDecomposition;

internal readonly record struct SvdDecompositionCacheKey(
    string ProxyFingerprint,
    SvdColorStrategy Strategy,
    ImageChannel SingleChannel,
    string NumericProtocol);

/// <summary>一个 Document 独占的源图、分析代理和有限分解缓存。</summary>
/// <remarks>
/// k 与分量索引不进入缓存键，因为二者只投影既有因子。源图或代理档位变化时直接替换整个 Session，
/// 比通用 LRU/计算图更容易证明不会跨 Document 泄漏。释放后清空缓存并阻断后续读取。
/// </remarks>
internal sealed class SvdSession : IDisposable
{
    private readonly Dictionary<SvdDecompositionCacheKey, SvdDecompositionSet> _cache = [];
    private bool _disposed;

    public SvdSession(string sourcePath, PixelImage sourceImage, PixelImage analysisProxy,
        int analysisMaximumEdge, string proxyFingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        SourcePath = sourcePath;
        SourceImage = sourceImage ?? throw new ArgumentNullException(nameof(sourceImage));
        AnalysisProxy = analysisProxy ?? throw new ArgumentNullException(nameof(analysisProxy));
        AnalysisMaximumEdge = analysisMaximumEdge;
        ProxyFingerprint = proxyFingerprint;
    }

    public string SourcePath { get; }
    public PixelImage SourceImage { get; }
    public PixelImage AnalysisProxy { get; }
    public int AnalysisMaximumEdge { get; }
    public string ProxyFingerprint { get; }
    public bool IsDisposed => _disposed;
    internal int CachedDecompositionCount => _cache.Count;

    public void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SvdSession));
    }

    internal bool TryGet(SvdDecompositionCacheKey key, out SvdDecompositionSet decomposition)
    {
        ThrowIfDisposed();
        return _cache.TryGetValue(key, out decomposition!);
    }

    internal void Add(SvdDecompositionCacheKey key, SvdDecompositionSet decomposition)
    {
        ThrowIfDisposed();
        _cache[key] = decomposition;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cache.Clear();
    }
}

internal sealed record SvdExperimentReport(
    string Schema,
    string NumericProtocol,
    string SourcePath,
    ImageSize SourceSize,
    ImageSize ProxySize,
    int AnalysisMaximumEdge,
    SvdDecompositionSet Decomposition,
    SvdRankResult RankResult,
    SvdComponentProjection? Component,
    SvdStrategyComparison? Comparison,
    IReadOnlyList<string> Limitations,
    DateTimeOffset CreatedAtUtc);

internal interface IPrepareSvdSessionUseCase
{
    Task<SvdSession> ExecuteAsync(string sourcePath, int analysisMaximumEdge, CancellationToken cancellationToken);
}

internal interface IDecomposeSvdUseCase
{
    Task<SvdDecompositionSet> ExecuteAsync(SvdSession session, SvdColorStrategy strategy,
        ImageChannel singleChannel, CancellationToken cancellationToken);
}

internal interface IReconstructSvdRankUseCase
{
    Task<SvdRankResult> ExecuteAsync(SvdSession session, SvdDecompositionSet decomposition,
        int rank, CancellationToken cancellationToken);
}

internal interface IProjectSvdComponentUseCase
{
    Task<SvdComponentProjection> ExecuteAsync(SvdDecompositionSet decomposition,
        int channelIndex, int componentIndex, CancellationToken cancellationToken);
}

internal interface ICompareSvdStrategiesUseCase
{
    Task<SvdStrategyComparison> ExecuteAsync(SvdSession session, int rank, CancellationToken cancellationToken);
}

internal interface IExportSvdImageUseCase
{
    Task ExecuteAsync(SvdSession session, SvdRankResult result, string expectedFingerprint,
        string outputPath, CancellationToken cancellationToken);
}

internal interface IExportSvdReportUseCase
{
    Task ExecuteAsync(SvdExperimentReport report, string outputPath, bool csv, CancellationToken cancellationToken);
}

internal interface ISvdReportSerializer
{
    byte[] SerializeJson(SvdExperimentReport report);
    byte[] SerializeCsv(SvdExperimentReport report);
}
