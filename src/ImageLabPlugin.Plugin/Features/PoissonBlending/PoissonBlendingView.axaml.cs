using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ImageLabPlugin.Features.PoissonBlending;

internal sealed partial class PoissonBlendingView : UserControl
{
    public PoissonBlendingView()
    {
        AvaloniaXamlLoader.Load(this);
        var canvas = this.FindControl<PoissonSourceMaskCanvas>("SourceMaskCanvas");
        if (canvas is not null) canvas.StrokeCompleted += (_, points) => (DataContext as PoissonBlendingDocument)?.AddStroke(points);
        var placement = this.FindControl<PoissonPlacementCanvas>("PlacementCanvas");
        if (placement is not null) placement.OffsetCommitted += (_, offset) =>
        {
            if (DataContext is not PoissonBlendingDocument document) return;
            document.OffsetX = offset.Dx; document.OffsetY = offset.Dy;
        };
    }
}
