using MyAvaloniaManagement.PluginSdk;

namespace ImageLabPlugin.Standalone;

/// <summary>每个 Standalone Document Scope 各自拥有关闭信号。</summary>
internal sealed class PreviewDocumentLifetime : IDocumentLifetime, IDisposable
{
    private readonly CancellationTokenSource _closing = new();

    public CancellationToken ClosingToken => _closing.Token;
    public bool IsClosing => _closing.IsCancellationRequested;

    public void Dispose()
    {
        if (!_closing.IsCancellationRequested)
        {
            _closing.Cancel();
        }

        _closing.Dispose();
    }
}
