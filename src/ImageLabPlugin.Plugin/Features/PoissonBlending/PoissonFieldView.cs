using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace ImageLabPlugin.Features.PoissonBlending;

/// <summary>只绘制已投影的有限 byte 热图；显示归一化不反馈给数值核心。</summary>
internal sealed class PoissonFieldView : Control
{
    public static readonly StyledProperty<Bitmap?> ImageProperty = AvaloniaProperty.Register<PoissonFieldView, Bitmap?>(nameof(Image));
    static PoissonFieldView() => AffectsRender<PoissonFieldView>(ImageProperty);
    public Bitmap? Image { get => GetValue(ImageProperty); set => SetValue(ImageProperty, value); }
    public override void Render(DrawingContext context) { base.Render(context); context.FillRectangle(Brushes.Black, Bounds); if (Image is not null) context.DrawImage(Image, new Rect(Image.Size), Bounds); }
}
