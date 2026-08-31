using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Input;
using ImageLabPlugin.Domain.PoissonBlending;

namespace ImageLabPlugin.Features.PoissonBlending;

/// <summary>显示目标图和映射后的遮罩包围盒；只作轻量放置反馈，不重建方程。</summary>
internal sealed class PoissonPlacementCanvas : Control
{
    public static readonly StyledProperty<Bitmap?> ImageProperty = AvaloniaProperty.Register<PoissonPlacementCanvas, Bitmap?>(nameof(Image));
    public static readonly StyledProperty<PoissonMaskTopology?> TopologyProperty = AvaloniaProperty.Register<PoissonPlacementCanvas, PoissonMaskTopology?>(nameof(Topology));
    public static readonly StyledProperty<int> OffsetXProperty = AvaloniaProperty.Register<PoissonPlacementCanvas, int>(nameof(OffsetX));
    public static readonly StyledProperty<int> OffsetYProperty = AvaloniaProperty.Register<PoissonPlacementCanvas, int>(nameof(OffsetY));
    static PoissonPlacementCanvas() => AffectsRender<PoissonPlacementCanvas>(ImageProperty, TopologyProperty, OffsetXProperty, OffsetYProperty);
    public Bitmap? Image { get => GetValue(ImageProperty); set => SetValue(ImageProperty, value); }
    public PoissonMaskTopology? Topology { get => GetValue(TopologyProperty); set => SetValue(TopologyProperty, value); }
    public int OffsetX { get => GetValue(OffsetXProperty); set => SetValue(OffsetXProperty, value); }
    public int OffsetY { get => GetValue(OffsetYProperty); set => SetValue(OffsetYProperty, value); }
    private Point _dragStart;
    private int _startOffsetX;
    private int _startOffsetY;
    private bool _dragging;
    public event EventHandler<ImageOffset>? OffsetCommitted;
    public override void Render(DrawingContext context)
    {
        base.Render(context); context.FillRectangle(Brushes.Black, Bounds); if (Image is null) return; context.DrawImage(Image, new Rect(Image.Size), Bounds);
        if (Topology is not { UnknownCount: > 0 } topology) return; var box = topology.BoundingBox;
        var rect = new Rect((box.Left + OffsetX) / (double)Image.PixelSize.Width * Bounds.Width,
            (box.Top + OffsetY) / (double)Image.PixelSize.Height * Bounds.Height,
            box.Width / (double)Image.PixelSize.Width * Bounds.Width, box.Height / (double)Image.PixelSize.Height * Bounds.Height);
        var legal = rect.Left > 0 && rect.Top > 0 && rect.Right < Bounds.Width && rect.Bottom < Bounds.Height;
        var brush = legal ? Brushes.LimeGreen : Brushes.OrangeRed;
        context.DrawRectangle(new SolidColorBrush(legal ? Color.FromArgb(42, 50, 205, 50) : Color.FromArgb(72, 255, 69, 0)),
            new Pen(brush, 2, dashStyle: legal ? null : new DashStyle([3, 3], 0)), rect);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e); if (Image is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _dragging = true; _dragStart = e.GetPosition(this); _startOffsetX = OffsetX; _startOffsetY = OffsetY; e.Pointer.Capture(this); e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e); if (!_dragging || Image is null || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var end = e.GetPosition(this); _dragging = false; e.Pointer.Capture(null);
        // 拖动结束才按目标像素量化并提交；中途不重建问题。ToEven 与领域整数坐标协议保持一致。
        var dx = (int)Math.Round((end.X - _dragStart.X) / Bounds.Width * Image.PixelSize.Width, MidpointRounding.ToEven);
        var dy = (int)Math.Round((end.Y - _dragStart.Y) / Bounds.Height * Image.PixelSize.Height, MidpointRounding.ToEven);
        OffsetCommitted?.Invoke(this, new ImageOffset(checked(_startOffsetX + dx), checked(_startOffsetY + dy))); e.Handled = true;
    }
}
