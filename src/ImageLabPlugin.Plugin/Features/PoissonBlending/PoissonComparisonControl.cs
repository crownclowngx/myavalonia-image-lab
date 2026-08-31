using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace ImageLabPlugin.Features.PoissonBlending;

/// <summary>并排显示直接 Alpha 与 Poisson 结果，不在控件内计算指标或暗示质量排名。</summary>
internal sealed class PoissonComparisonControl : Control
{
    public static readonly StyledProperty<Bitmap?> AlphaImageProperty = AvaloniaProperty.Register<PoissonComparisonControl, Bitmap?>(nameof(AlphaImage));
    public static readonly StyledProperty<Bitmap?> PoissonImageProperty = AvaloniaProperty.Register<PoissonComparisonControl, Bitmap?>(nameof(PoissonImage));
    static PoissonComparisonControl() => AffectsRender<PoissonComparisonControl>(AlphaImageProperty, PoissonImageProperty);
    public Bitmap? AlphaImage { get => GetValue(AlphaImageProperty); set => SetValue(AlphaImageProperty, value); }
    public Bitmap? PoissonImage { get => GetValue(PoissonImageProperty); set => SetValue(PoissonImageProperty, value); }
    public override void Render(DrawingContext context) { base.Render(context); context.FillRectangle(Brushes.Black, Bounds); var half = Bounds.Width / 2d; if (AlphaImage is not null) context.DrawImage(AlphaImage, new Rect(AlphaImage.Size), new Rect(0, 0, half, Bounds.Height)); if (PoissonImage is not null) context.DrawImage(PoissonImage, new Rect(PoissonImage.Size), new Rect(half, 0, half, Bounds.Height)); context.DrawLine(new Pen(Brushes.White, 1), new Point(half, 0), new Point(half, Bounds.Height)); }
}
