using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace ImageLabPlugin.Features.SeamCarving;

/// <summary>只负责把能量灰度预览等比绘制到控件区域。</summary>
internal sealed class EnergyMapControl : Control
{
    public static readonly StyledProperty<Bitmap?> ImageProperty =
        AvaloniaProperty.Register<EnergyMapControl, Bitmap?>(nameof(Image));
    static EnergyMapControl() => AffectsRender<EnergyMapControl>(ImageProperty);
    public Bitmap? Image { get => GetValue(ImageProperty); set => SetValue(ImageProperty, value); }
    public override void Render(DrawingContext context)
    {
        base.Render(context); context.FillRectangle(Brushes.Black, Bounds);
        if (Image is not null) context.DrawImage(Image, new Rect(Image.Size), Bounds);
    }
}
