using System.Security.Cryptography;
using ImageLabPlugin.Domain.ImageOscilloscope;
using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Application.ImageOscilloscope;

/// <summary>单个图像示波器 Document 独占的源图、主分析、代理和当前裁切结果。</summary>
/// <remarks>
/// Session 不持有路径、Avalonia Bitmap、文件端口或 View。主分析构造后不再变化；阈值重算使用独立
/// clipping generation，候选提交同时校验 generation 与源指纹。失败、取消或迟到候选均保留最后有效覆盖层。
/// </remarks>
internal sealed class ImageOscilloscopeSession : IDisposable
{
    private readonly object _sync = new();
    private long _clippingGeneration;
    private bool _disposed;

    public ImageOscilloscopeSession(PixelImage source, PixelImage preview,
        ImageOscilloscopeAnalysis analysis, ClippingAnalysis clipping)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Preview = preview ?? throw new ArgumentNullException(nameof(preview));
        Analysis = analysis ?? throw new ArgumentNullException(nameof(analysis));
        CurrentClipping = clipping ?? throw new ArgumentNullException(nameof(clipping));
        SourceFingerprint = Convert.ToHexString(SHA256.HashData(source.Rgba.Span))[..24];
    }

    internal PixelImage Source { get; }
    public PixelImage Preview { get; }
    public ImageOscilloscopeAnalysis Analysis { get; }
    public ClippingAnalysis CurrentClipping { get; private set; }
    public string SourceFingerprint { get; }

    public long AdvanceClippingGeneration()
    {
        lock (_sync) { ThrowIfDisposed(); return ++_clippingGeneration; }
    }

    public bool TryCommitClipping(ClippingAnalysis candidate, long generation, string expectedFingerprint)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        lock (_sync)
        {
            ThrowIfDisposed();
            if (generation != _clippingGeneration || !StringComparer.Ordinal.Equals(expectedFingerprint, SourceFingerprint)) return false;
            CurrentClipping = candidate;
            return true;
        }
    }

    public void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ImageOscilloscopeSession));
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _clippingGeneration++;
        }
    }
}
