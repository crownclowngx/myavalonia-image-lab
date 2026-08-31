using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace ImageLabPlugin.Features.SeamCarving;

/// <summary>并排显示 Seam 与规则网格参考结果；不在控件内计算指标或宣称质量排名。</summary>
internal sealed class SeamComparisonControl : Control
{
    public static readonly StyledProperty<Bitmap?> SeamImageProperty =
        AvaloniaProperty.Register<SeamComparisonControl, Bitmap?>(nameof(SeamImage));
    public static readonly StyledProperty<Bitmap?> ReferenceImageProperty =
        AvaloniaProperty.Register<SeamComparisonControl, Bitmap?>(nameof(ReferenceImage));
    static SeamComparisonControl() => AffectsRender<SeamComparisonControl>(SeamImageProperty, ReferenceImageProperty);
    public Bitmap? SeamImage { get => GetValue(SeamImageProperty); set => SetValue(SeamImageProperty, value); }
    public Bitmap? ReferenceImage { get => GetValue(ReferenceImageProperty); set => SetValue(ReferenceImageProperty, value); }
    public override void Render(DrawingContext context)
    {
        base.Render(context); context.FillRectangle(Brushes.Black, Bounds);
        var half = Bounds.Width / 2d;
        if (SeamImage is not null) context.DrawImage(SeamImage, new Rect(SeamImage.Size), new Rect(0, 0, half, Bounds.Height));
        if (ReferenceImage is not null) context.DrawImage(ReferenceImage, new Rect(ReferenceImage.Size), new Rect(half, 0, half, Bounds.Height));
        context.DrawLine(new Pen(Brushes.White, 1d), new Point(half, 0), new Point(half, Bounds.Height));
    }
}
