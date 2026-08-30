using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ImageLabPlugin.Domain.FrequencyMaskEditing;

namespace ImageLabPlugin.Features.FrequencyMaskEditor;

internal sealed class FrequencyMaskGestureEventArgs(IReadOnlyList<NormalizedFrequencyPoint> points) : EventArgs
{
    public IReadOnlyList<NormalizedFrequencyPoint> Points { get; } = points;
}

internal sealed class FrequencyMaskHoverEventArgs(double x, double y) : EventArgs
{
    public double X { get; } = x;
    public double Y { get; } = y;
}

/// <summary>绘制频谱和遮罩覆盖层，并仅转发归一化指针意图。</summary>
/// <remarks>
/// 控件不知道 FFT 自然索引、共轭公式、增益数组和历史；letterbox、DPI 与 Pointer capture 是它唯一负责的边界。
/// </remarks>
internal sealed class FrequencyMaskCanvasControl : Control
{
    private readonly List<NormalizedFrequencyPoint> _gesture = [];
    private bool _dragging;

    public static readonly StyledProperty<Bitmap?> SpectrumProperty =
        AvaloniaProperty.Register<FrequencyMaskCanvasControl, Bitmap?>(nameof(Spectrum));
    public static readonly StyledProperty<Bitmap?> MaskProperty =
        AvaloniaProperty.Register<FrequencyMaskCanvasControl, Bitmap?>(nameof(Mask));
    public static readonly StyledProperty<double> MaskOpacityProperty =
        AvaloniaProperty.Register<FrequencyMaskCanvasControl, double>(nameof(MaskOpacity), 0.55d);
    public static readonly StyledProperty<double> ProbeXProperty =
        AvaloniaProperty.Register<FrequencyMaskCanvasControl, double>(nameof(ProbeX), -1d);
    public static readonly StyledProperty<double> ProbeYProperty =
        AvaloniaProperty.Register<FrequencyMaskCanvasControl, double>(nameof(ProbeY), -1d);
    public static readonly StyledProperty<double> MirrorXProperty =
        AvaloniaProperty.Register<FrequencyMaskCanvasControl, double>(nameof(MirrorX), -1d);
    public static readonly StyledProperty<double> MirrorYProperty =
        AvaloniaProperty.Register<FrequencyMaskCanvasControl, double>(nameof(MirrorY), -1d);

    static FrequencyMaskCanvasControl()
    {
        AffectsRender<FrequencyMaskCanvasControl>(SpectrumProperty, MaskProperty, MaskOpacityProperty,
            ProbeXProperty, ProbeYProperty, MirrorXProperty, MirrorYProperty);
        FocusableProperty.OverrideDefaultValue<FrequencyMaskCanvasControl>(true);
    }

    public Bitmap? Spectrum { get => GetValue(SpectrumProperty); set => SetValue(SpectrumProperty, value); }
    public Bitmap? Mask { get => GetValue(MaskProperty); set => SetValue(MaskProperty, value); }
    public double MaskOpacity { get => GetValue(MaskOpacityProperty); set => SetValue(MaskOpacityProperty, value); }
    public double ProbeX { get => GetValue(ProbeXProperty); set => SetValue(ProbeXProperty, value); }
    public double ProbeY { get => GetValue(ProbeYProperty); set => SetValue(ProbeYProperty, value); }
    public double MirrorX { get => GetValue(MirrorXProperty); set => SetValue(MirrorXProperty, value); }
    public double MirrorY { get => GetValue(MirrorYProperty); set => SetValue(MirrorYProperty, value); }
    public event EventHandler<FrequencyMaskGestureEventArgs>? GestureCompleted;
    public event EventHandler<FrequencyMaskHoverEventArgs>? Hovered;

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(Brushes.Black, Bounds);
        var image = Spectrum ?? Mask;
        if (image is null || !FrequencyCanvasCoordinateMapper.TryGetImageRect(Bounds.Size, image.PixelSize, out var destination)) return;
        if (Spectrum is not null) context.DrawImage(Spectrum, new Rect(Spectrum.Size), destination);
        if (Mask is not null)
        {
            using (context.PushOpacity(Math.Clamp(MaskOpacity, 0d, 1d)))
                context.DrawImage(Mask, new Rect(Mask.Size), destination);
        }
        context.DrawRectangle(new Pen(Brushes.Gray, 1d), destination);
        DrawGesturePreview(context, destination);
        DrawCross(context, destination, ProbeX, ProbeY, Brushes.Yellow);
        DrawCross(context, destination, MirrorX, MirrorY, Brushes.Cyan);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || !TryMap(e.GetPosition(this), out var point)) return;
        _gesture.Clear();
        _gesture.Add(point);
        _dragging = true;
        InvalidateVisual();
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!TryMap(e.GetPosition(this), out var point)) return;
        Hovered?.Invoke(this, new FrequencyMaskHoverEventArgs(point.X, point.Y));
        if (!_dragging || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var previous = _gesture[^1];
        if (Math.Sqrt(Math.Pow(point.X - previous.X, 2d) + Math.Pow(point.Y - previous.Y, 2d)) < 0.0005d) return;
        if (_gesture.Count < FrequencyMaskOperation.MaximumStrokePoints) _gesture.Add(point);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging) return;
        if (TryMap(e.GetPosition(this), out var point) && _gesture[^1] != point && _gesture.Count < FrequencyMaskOperation.MaximumStrokePoints)
            _gesture.Add(point);
        _dragging = false;
        e.Pointer.Capture(null);
        GestureCompleted?.Invoke(this, new FrequencyMaskGestureEventArgs(_gesture.ToArray()));
        _gesture.Clear();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragging = false;
        _gesture.Clear();
        InvalidateVisual();
    }

    private bool TryMap(Point position, out NormalizedFrequencyPoint point)
    {
        point = default;
        var bitmap = Spectrum ?? Mask;
        if (bitmap is null || !FrequencyCanvasCoordinateMapper.TryMap(Bounds.Size, bitmap.PixelSize, position, out var x, out var y)) return false;
        point = new NormalizedFrequencyPoint(x, y);
        return true;
    }

    private void DrawGesturePreview(DrawingContext context, Rect destination)
    {
        if (_gesture.Count == 0) return;
        var pen = new Pen(Brushes.Orange, 2d);
        var previous = ToCanvas(destination, _gesture[0]);
        context.DrawEllipse(null, pen, previous, 3d, 3d);
        for (var i = 1; i < _gesture.Count; i++)
        {
            var current = ToCanvas(destination, _gesture[i]);
            context.DrawLine(pen, previous, current);
            previous = current;
        }
    }

    private static void DrawCross(DrawingContext context, Rect destination, double x, double y, IBrush brush)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || x is < 0d or > 1d || y is < 0d or > 1d) return;
        var center = new Point(destination.X + (x * destination.Width), destination.Y + (y * destination.Height));
        var pen = new Pen(brush, 1.5d);
        context.DrawLine(pen, new Point(center.X - 7, center.Y), new Point(center.X + 7, center.Y));
        context.DrawLine(pen, new Point(center.X, center.Y - 7), new Point(center.X, center.Y + 7));
    }

    private static Point ToCanvas(Rect destination, NormalizedFrequencyPoint point) =>
        new(destination.X + (point.X * destination.Width), destination.Y + (point.Y * destination.Height));
}
