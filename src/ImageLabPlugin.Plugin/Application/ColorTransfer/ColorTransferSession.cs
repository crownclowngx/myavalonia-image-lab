using ImageLabPlugin.Domain.ColorTransfer;

namespace ImageLabPlugin.Application.ColorTransfer;

/// <summary>一个 Document Scope 独占的颜色实验状态与所有权边界。</summary>
/// <remarks>
/// Session 拥有目标、参考、分析、冻结调色板和一个当前结果。无状态数学服务不会缓存这些对象；换图时
/// 对应分析、palette 和结果原子失效，保证旧 fingerprint 不能被导出。PixelImage 使用托管数组，无需释放；
/// Avalonia Bitmap 与取消源仍由 Document 负责释放。
/// </remarks>
internal sealed class ColorTransferSession : IDisposable
{
    private bool _disposed;
    public PreparedColorImage? Target { get; private set; }
    public PreparedColorImage? Reference { get; private set; }
    public ColorAnalysisResult? TargetAnalysis { get; private set; }
    public ColorAnalysisResult? ReferenceAnalysis { get; private set; }
    public FrozenPalette? FrozenPalette { get; private set; }
    public ColorOperationResult? Result { get; private set; }
    public long Revision { get; private set; }
    public long ResultRevision { get; private set; } = -1;
    public bool HasCurrentResult => Result is not null && ResultRevision == Revision;

    public void SetTarget(PreparedColorImage value)
    { ThrowIfDisposed(); Target = value; TargetAnalysis = null; FrozenPalette = null; InvalidateResult(); }
    public void SetReference(PreparedColorImage value)
    { ThrowIfDisposed(); Reference = value; ReferenceAnalysis = null; if (FrozenPalette?.Source == PaletteSource.Reference) FrozenPalette = null; InvalidateResult(); }
    public void SetAnalysis(ColorAnalysisResult value, PaletteSource source)
    { ThrowIfDisposed(); if (source == PaletteSource.Target) TargetAnalysis = value; else ReferenceAnalysis = value; }
    public void SetFrozenPalette(FrozenPalette value) { ThrowIfDisposed(); FrozenPalette = value; InvalidateResult(); }
    public void ChangeAnalysisRecipe()
    { ThrowIfDisposed(); TargetAnalysis = null; ReferenceAnalysis = null; FrozenPalette = null; InvalidateResult(); }
    public void ChangeRecipe() { ThrowIfDisposed(); InvalidateResult(); }
    public void CommitResult(ColorOperationResult value) { ThrowIfDisposed(); Result = value; ResultRevision = Revision; }

    public void Dispose()
    { if (_disposed) return; _disposed = true; Target = null; Reference = null; TargetAnalysis = null; ReferenceAnalysis = null; FrozenPalette = null; Result = null; }
    private void InvalidateResult() { Revision++; ResultRevision = -1; }
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
