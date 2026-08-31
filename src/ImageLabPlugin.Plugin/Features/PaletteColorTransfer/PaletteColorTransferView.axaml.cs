using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ImageLabPlugin.Features.PaletteColorTransfer;

internal sealed partial class PaletteColorTransferView : UserControl
{
    public PaletteColorTransferView() => AvaloniaXamlLoader.Load(this);
}
