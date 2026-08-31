using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ImageLabPlugin.Domain.PoissonBlending;

namespace ImageLabPlugin.Features.PoissonBlending;

/// <summary>显示源图并把指针轨迹转换为归一化点；控件不栅格化遮罩，也不接触 Poisson 数值。</summary>
internal sealed class PoissonSourceMaskCanvas : Control
{
    public static readonly StyledProperty<Bitmap?> ImageProperty = AvaloniaProperty.Register<PoissonSourceMaskCanvas, Bitmap?>(nameof(Image));
    public static readonly StyledProperty<PoissonMaskTopology?> TopologyProperty = AvaloniaProperty.Register<PoissonSourceMaskCanvas, PoissonMaskTopology?>(nameof(Topology));
    private readonly List<PoissonNormalizedPoint> _points = [];
    private bool _drawing;
    static PoissonSourceMaskCanvas() => AffectsRender<PoissonSourceMaskCanvas>(ImageProperty, TopologyProperty);
    public Bitmap? Image { get => GetValue(ImageProperty); set => SetValue(ImageProperty, value); }
    public PoissonMaskTopology? Topology { get => GetValue(TopologyProperty); set => SetValue(TopologyProperty, value); }
    public event EventHandler<IReadOnlyList<PoissonNormalizedPoint>>? StrokeCompleted;
    public override void Render(DrawingContext context)
    {
        base.Render(context); context.FillRectangle(Brushes.Black, Bounds); if (Image is null) return;
        context.DrawImage(Image, new Rect(Image.Size), Bounds);
        if (Topology is { UnknownCount: > 0 } topology)
        {
            var box = topology.BoundingBox; var rect = new Rect(box.Left / (double)Image.PixelSize.Width * Bounds.Width,
                box.Top / (double)Image.PixelSize.Height * Bounds.Height, box.Width / (double)Image.PixelSize.Width * Bounds.Width,
                box.Height / (double)Image.PixelSize.Height * Bounds.Height);
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(48, 0, 220, 255)), new Pen(Brushes.Cyan, 2, dashStyle: new DashStyle([5, 3], 0)), rect);
        }
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e) { base.OnPointerPressed(e); if (Image is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return; _drawing = true; _points.Clear(); Add(e.GetPosition(this)); e.Pointer.Capture(this); e.Handled = true; }
    protected override void OnPointerMoved(PointerEventArgs e) { base.OnPointerMoved(e); if (_drawing) { Add(e.GetPosition(this)); e.Handled = true; } }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { base.OnPointerReleased(e); if (!_drawing) return; Add(e.GetPosition(this)); _drawing = false; e.Pointer.Capture(null); StrokeCompleted?.Invoke(this, _points.ToArray()); e.Handled = true; }
    private void Add(Point point)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var value = new PoissonNormalizedPoint(Math.Clamp(point.X / Bounds.Width, 0d, 1d), Math.Clamp(point.Y / Bounds.Height, 0d, 1d));
        if (_points.Count == 0 || Math.Abs(_points[^1].X - value.X) + Math.Abs(_points[^1].Y - value.Y) >= 0.002d) _points.Add(value);
    }
}
