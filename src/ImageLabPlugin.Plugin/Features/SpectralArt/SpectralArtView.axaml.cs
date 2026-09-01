using Avalonia.Controls;

namespace ImageLabPlugin.Features.SpectralArt;

internal sealed partial class SpectralArtView : UserControl
{
    public SpectralArtView()
    {
        InitializeComponent();
        var canvas = this.FindControl<SpectralArtRegionCanvas>("RegionCanvas");
        if (canvas is not null) canvas.RegionChanged += (_, value) =>
        {
            if (DataContext is not SpectralArtDocument document) return;
            document.RegionLeft = value.Left; document.RegionTop = value.Top;
            document.RegionRight = value.Right; document.RegionBottom = value.Bottom;
        };
    }
}
