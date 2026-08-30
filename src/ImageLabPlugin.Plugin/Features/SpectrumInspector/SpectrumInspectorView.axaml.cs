using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;

namespace ImageLabPlugin.Features.SpectrumInspector;

public sealed partial class SpectrumInspectorView : UserControl
{
    public SpectrumInspectorView() => InitializeComponent();

    private void OnSourcePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is SpectrumInspectorDocument document && sender is Image image && TryNormalize(image, e.GetPosition(image), out var x, out var y))
            document.InspectSourceAt(x, y);
    }

    private void OnSpectrumPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is SpectrumInspectorDocument document && sender is Image image && TryNormalize(image, e.GetPosition(image), out var x, out var y))
            document.InspectFrequencyAt(x, y);
    }

    /// <summary>把 Uniform 图片的有效绘制区域映射到 [0,1)，明确排除面板黑边。</summary>
    internal static bool TryNormalize(Image image, Point point, out double normalizedX, out double normalizedY)
    {
        normalizedX = normalizedY = 0d;
        if (image.Source is not Bitmap bitmap || image.Bounds.Width <= 0d || image.Bounds.Height <= 0d) return false;
        var scale = Math.Min(image.Bounds.Width / bitmap.PixelSize.Width, image.Bounds.Height / bitmap.PixelSize.Height);
        var width = bitmap.PixelSize.Width * scale; var height = bitmap.PixelSize.Height * scale;
        var left = (image.Bounds.Width - width) / 2d; var top = (image.Bounds.Height - height) / 2d;
        if (point.X < left || point.Y < top || point.X >= left + width || point.Y >= top + height) return false;
        normalizedX = Math.Clamp((point.X - left) / width, 0d, Math.BitDecrement(1d));
        normalizedY = Math.Clamp((point.Y - top) / height, 0d, Math.BitDecrement(1d));
        return true;
    }
}
