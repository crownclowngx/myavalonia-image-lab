using Avalonia.Controls;
using Avalonia.Input;

namespace ImageLabPlugin.Features.MagnitudePhaseSwap;

/// <summary>幅相交换视图只加载 AXAML；文件、FFT、指标、取消和资源所有权均由 Document/用例负责。</summary>
public sealed partial class MagnitudePhaseSwapView : UserControl
{
    public MagnitudePhaseSwapView() => InitializeComponent();

    private void OnSpectrumPointerMoved(object? sender, PointerEventArgs eventArgs)
    {
        if (sender is not Image image || DataContext is not MagnitudePhaseSwapDocument document) return;
        var point = eventArgs.GetPosition(image);
        var normalized = MagnitudePhaseCoordinateMapper.ToNormalized(point.X, point.Y,
            image.Bounds.Width, image.Bounds.Height, document.CanvasSize, document.CanvasSize);
        if (normalized.IsInside) document.UpdateProbe(normalized.X, normalized.Y);
    }
}
