using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace ImageLabPlugin.Features.ImageCompareLab;

public sealed partial class ImageCompareLabView : UserControl
{
    private Point? _panStart;
    private double _panCenterX;
    private double _panCenterY;
    private bool _splitDragging;

    public ImageCompareLabView() => InitializeComponent();

    private void OnViewportPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not ComparisonViewportControl viewport || DataContext is not ImageCompareLabDocument document) return;
        var point = e.GetCurrentPoint(viewport);
        if (point.Properties.IsRightButtonPressed)
        {
            _panStart = point.Position; _panCenterX = document.ViewportCenterX; _panCenterY = document.ViewportCenterY;
            e.Pointer.Capture(viewport); return;
        }
        if (document.DisplayMode == ComparisonDisplayMode.Split && point.Properties.IsLeftButtonPressed)
        {
            _splitDragging = true;
            document.SplitRatio = Math.Clamp(point.Position.X / Math.Max(1d, viewport.Bounds.Width), 0d, 1d);
            e.Pointer.Capture(viewport); return;
        }
        if (viewport.TryMap(point.Position, out var sourcePoint)) document.InspectProxyAt(sourcePoint);
    }

    private void OnViewportPointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not ComparisonViewportControl viewport || DataContext is not ImageCompareLabDocument document) return;
        var point = e.GetPosition(viewport);
        if (_splitDragging && e.GetCurrentPoint(viewport).Properties.IsLeftButtonPressed)
        {
            document.SplitRatio = Math.Clamp(point.X / Math.Max(1d, viewport.Bounds.Width), 0d, 1d); return;
        }
        if (_panStart is { } start && e.GetCurrentPoint(viewport).Properties.IsRightButtonPressed)
        {
            var scale = document.Zoom <= 0d ? 1d : document.Zoom;
            document.ViewportCenterX = Math.Clamp(_panCenterX - ((point.X - start.X) / Math.Max(1d, viewport.Bounds.Width * scale)), 0d, 1d);
            document.ViewportCenterY = Math.Clamp(_panCenterY - ((point.Y - start.Y) / Math.Max(1d, viewport.Bounds.Height * scale)), 0d, 1d);
            return;
        }
        if (viewport.TryMap(point, out var sourcePoint)) document.InspectProxyAt(sourcePoint);
    }

    private void OnViewportPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _panStart = null; _splitDragging = false; e.Pointer.Capture(null);
    }

    private void OnViewportPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not ImageCompareLabDocument document || sender is not ComparisonViewportControl viewport) return;
        var oldZoom = document.Zoom;
        var newZoom = e.Delta.Y > 0
            ? Math.Clamp(oldZoom <= 0d ? 1d : oldZoom * 2d, 0.25d, 16d)
            : Math.Clamp(oldZoom <= 0d ? 0.5d : oldZoom / 2d, 0.25d, 16d);
        if (viewport.TryCalculateAnchoredCenter(e.GetPosition(viewport), newZoom, out var centerX, out var centerY))
        {
            document.ViewportCenterX = centerX; document.ViewportCenterY = centerY;
        }
        document.Zoom = newZoom;
        e.Handled = true;
    }
}
