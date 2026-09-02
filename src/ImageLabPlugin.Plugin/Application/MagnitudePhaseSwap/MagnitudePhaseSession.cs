using ImageLabPlugin.Domain.Frequency;
using ImageLabPlugin.Domain.Imaging;
using ImageLabPlugin.Domain.MagnitudePhaseSwap;

namespace ImageLabPlugin.Application.MagnitudePhaseSwap;

/// <summary>单个 Document Scope 独占的 A/B 规范画布、频谱、显示摘要和最后有效结果。</summary>
/// <remarks>
/// Session 是大缓冲的唯一长期所有者，不持有文件端口、Avalonia Bitmap、Document 或 View。所有候选携带
/// Session/Recipe 指纹与 generation；只有四项仍一致才提交，取消、失败和迟到候选不会清除最后有效结果。
/// Dispose 推进 generation 并拒绝后续访问，多实例之间不存在静态缓存。
/// </remarks>
internal sealed class MagnitudePhaseSession : IDisposable
{
    private readonly object _sync = new();
    private long _generation;
    private bool _disposed;

    public MagnitudePhaseSession(string pathA, string pathB, FrequencyPairCanvas canvasA,
        FrequencyPairCanvas canvasB, FrequencySpectrum spectrumA, FrequencySpectrum spectrumB,
        PixelImage previewA, PixelImage previewB, PixelImage magnitudeA, PixelImage magnitudeB,
        PixelImage phaseA, PixelImage phaseB)
    {
        PathA = pathA; PathB = pathB; CanvasA = canvasA; CanvasB = canvasB;
        SpectrumA = spectrumA; SpectrumB = spectrumB; PreviewA = previewA; PreviewB = previewB;
        MagnitudeA = magnitudeA; MagnitudeB = magnitudeB; PhaseA = phaseA; PhaseB = phaseB;
        PhaseThresholdA = PhaseThreshold(spectrumA); PhaseThresholdB = PhaseThreshold(spectrumB);
        SessionFingerprint = $"{canvasA.Fingerprint}:{canvasB.Fingerprint}:{canvasA.Size}";
    }

    public string PathA { get; }
    public string PathB { get; }
    public FrequencyPairCanvas CanvasA { get; }
    public FrequencyPairCanvas CanvasB { get; }
    public FrequencySpectrum SpectrumA { get; }
    public FrequencySpectrum SpectrumB { get; }
    public PixelImage PreviewA { get; }
    public PixelImage PreviewB { get; }
    public PixelImage MagnitudeA { get; }
    public PixelImage MagnitudeB { get; }
    public PixelImage PhaseA { get; }
    public PixelImage PhaseB { get; }
    public string FingerprintA => CanvasA.Fingerprint;
    public string FingerprintB => CanvasB.Fingerprint;
    public string SessionFingerprint { get; }
    public double PhaseThresholdA { get; }
    public double PhaseThresholdB { get; }
    public MagnitudePhaseRenderResult? CurrentResult { get; private set; }
    public long Generation { get { lock (_sync) { ThrowIfDisposed(); return _generation; } } }

    public long AdvanceGeneration() { lock (_sync) { ThrowIfDisposed(); return ++_generation; } }

    public bool TryCommit(MagnitudePhaseRenderResult candidate, long expectedGeneration,
        string expectedRecipeFingerprint)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_generation != expectedGeneration || candidate.Generation != expectedGeneration ||
                !StringComparer.Ordinal.Equals(candidate.SessionFingerprint, SessionFingerprint) ||
                !StringComparer.Ordinal.Equals(candidate.RecipeFingerprint, expectedRecipeFingerprint)) return false;
            CurrentResult = candidate;
            return true;
        }
    }

    public void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MagnitudePhaseSession));
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _generation++;
            CurrentResult = null;
        }
    }

    private static double PhaseThreshold(FrequencySpectrum spectrum)
    {
        double maximum = 0d; foreach (var value in spectrum.Values.Span) maximum = Math.Max(maximum, value.Magnitude);
        return Math.Max(1e-12, maximum * 1e-12);
    }
}

/// <summary>在创建两份频谱和一份 IFFT 工作副本前执行 checked 工作集门禁。</summary>
internal sealed class MagnitudePhaseResourceEstimator
{
    public const long MaximumEstimatedBytes = 256L * 1024 * 1024;

    public long EstimateBytes(int canvasSize)
    {
        MagnitudePhaseCanvasSize.Validate(canvasSize);
        var pixels = checked((long)canvasSize * canvasSize);
        // 两画布、两只读频谱、一工作频谱、raw、六张 RGBA 预览及保守临时列缓冲。
        return checked(pixels * ((8L * 3L) + (16L * 3L) + (4L * 7L)));
    }

    public void EnsureWithinBudget(int canvasSize)
    {
        var bytes = EstimateBytes(canvasSize);
        if (bytes > MaximumEstimatedBytes)
            throw new InvalidOperationException($"预计工作集 {bytes / (1024d * 1024d):F1} MiB 超出 256 MiB 门禁。");
    }
}
