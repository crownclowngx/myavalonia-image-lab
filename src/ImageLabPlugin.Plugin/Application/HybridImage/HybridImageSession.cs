using ImageLabPlugin.Domain.HybridImage;
using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Application.HybridImage;

internal sealed record HybridAlignmentState(
    HybridAlignmentSolution Solution,
    HybridCropRectangle MaximumCrop,
    double CoverageRatio,
    string PointFingerprint);

/// <summary>一个 Document Scope 独占的双输入、代理、对齐和最后有效结果。</summary>
/// <remarks>
/// Session 是资源所有者但不认识 Avalonia Bitmap、文件对话框或 JSON DTO。generation、Session 引用身份和
/// recipe fingerprint 必须同时匹配才提交候选结果；取消、异常或忽略取消的迟到服务都不会破坏最后有效结果。
/// </remarks>
internal sealed class HybridImageSession : IDisposable
{
    private readonly object _sync = new();
    private bool _disposed;
    private long _generation;

    public HybridImageSession(string pathA, string pathB, PixelImage sourceA, PixelImage sourceB,
        PixelImage proxyA, PixelImage proxyB, HybridLumaPlane sourceLumaA, HybridLumaPlane sourceLumaB,
        HybridLumaPlane proxyLumaA, HybridLumaPlane proxyLumaB, string fingerprintA, string fingerprintB)
    {
        PathA = pathA;
        PathB = pathB;
        SourceA = sourceA;
        SourceB = sourceB;
        ProxyA = proxyA;
        ProxyB = proxyB;
        SourceLumaA = sourceLumaA;
        SourceLumaB = sourceLumaB;
        ProxyLumaA = proxyLumaA;
        ProxyLumaB = proxyLumaB;
        FingerprintA = fingerprintA;
        FingerprintB = fingerprintB;
        SessionFingerprint = Guid.NewGuid().ToString("N");
    }

    public string PathA { get; }
    public string PathB { get; }
    public PixelImage SourceA { get; }
    public PixelImage SourceB { get; }
    public PixelImage ProxyA { get; }
    public PixelImage ProxyB { get; }
    public HybridLumaPlane SourceLumaA { get; }
    public HybridLumaPlane SourceLumaB { get; }
    public HybridLumaPlane ProxyLumaA { get; }
    public HybridLumaPlane ProxyLumaB { get; }
    public string FingerprintA { get; }
    public string FingerprintB { get; }
    public string SessionFingerprint { get; }
    public HybridAlignmentState? Alignment { get; private set; }
    public HybridRenderResult? LastPreview { get; private set; }
    public HybridRenderResult? LastFullSize { get; private set; }
    public long Generation { get { lock (_sync) return _generation; } }
    public bool IsDisposed { get { lock (_sync) return _disposed; } }

    public long AdvanceGeneration()
    {
        lock (_sync) { ThrowIfDisposed(); return ++_generation; }
    }

    public void CommitAlignment(HybridAlignmentState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (_sync)
        {
            ThrowIfDisposed();
            Alignment = state;
            LastPreview = null;
            LastFullSize = null;
            _generation++;
        }
    }

    public bool TryCommit(HybridRenderResult candidate, long expectedGeneration, string expectedRecipeFingerprint)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_generation != expectedGeneration || candidate.Generation != expectedGeneration ||
                !StringComparer.Ordinal.Equals(candidate.SessionFingerprint, SessionFingerprint) ||
                !StringComparer.Ordinal.Equals(candidate.RecipeFingerprint, expectedRecipeFingerprint)) return false;
            if (candidate.IsFullSize) LastFullSize = candidate;
            else LastPreview = candidate;
            return true;
        }
    }

    public void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(HybridImageSession));
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _generation++;
            Alignment = null;
            LastPreview = null;
            LastFullSize = null;
        }
    }
}

/// <summary>在分配 warp、卷积和频谱大数组之前估算完整工作集。</summary>
internal sealed class HybridResourceEstimator
{
    public const long MaximumEstimatedBytes = 768L * 1024 * 1024;

    public long EstimateBytes(ImageSize size)
    {
        // A/B、warp、低/高/raw、两个卷积缓冲、掩码、RGBA 与一个临时复数频谱的保守峰值。
        return checked(size.PixelCount * ((8L * 9L) + 1L + 4L + 16L));
    }

    public void EnsureWithinBudget(ImageSize size, double lowSigma, double highSigma)
    {
        var bytes = EstimateBytes(size);
        if (bytes > MaximumEstimatedBytes)
            throw new InvalidOperationException($"预计工作集 {bytes / (1024d * 1024d):F1} MiB 超过 768 MiB 门禁。");
        var maximumRadius = Math.Max(Math.Ceiling(3d * lowSigma), Math.Ceiling(3d * highSigma));
        var work = checked((long)size.PixelCount * (((long)maximumRadius * 2L) + 1L) * 2L);
        if (work > GaussianPlaneFilter.MaximumWorkItems)
            throw new InvalidOperationException($"预计 Gaussian 工作量 {work:N0} 超过本地门禁。");
    }
}
