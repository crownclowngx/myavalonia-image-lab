using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ImageLabPlugin.Features.SeamCarving;

internal sealed partial class SeamCarvingView : UserControl
{
    public SeamCarvingView()
    {
        AvaloniaXamlLoader.Load(this);
        var overlay = this.FindControl<SeamOverlayCanvas>("Overlay");
        if (overlay is not null) overlay.StrokeCompleted += OnStrokeCompleted;
    }

    private void OnStrokeCompleted(object? sender, IReadOnlyList<Domain.SeamCarving.SeamNormalizedPoint> points)
    {
        if (DataContext is SeamCarvingDocument document) document.AddStroke(points);
    }
}
