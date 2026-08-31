using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ImageLabPlugin.Domain.SeamCarving;

namespace ImageLabPlugin.Features.SeamCarving;

/// <summary>显示当前工作图、下一条缝和可辨纹理，并把指针轨迹归一化后交给 Document。</summary>
/// <remarks>
/// 控件只做坐标映射和绘制，不计算能量、不寻找或应用缝。删除使用红色实线，插入使用青色虚线，
/// 即使用户无法区分红/青，也能由纹理和旁边的文字图例辨认操作。
/// </remarks>
internal sealed class SeamOverlayCanvas : Control
{
    public static readonly StyledProperty<Bitmap?> ImageProperty =
        AvaloniaProperty.Register<SeamOverlayCanvas, Bitmap?>(nameof(Image));
    public static readonly StyledProperty<IReadOnlyList<int>?> CoordinatesProperty =
        AvaloniaProperty.Register<SeamOverlayCanvas, IReadOnlyList<int>?>(nameof(Coordinates));
    public static readonly StyledProperty<SeamOrientation> OrientationProperty =
        AvaloniaProperty.Register<SeamOverlayCanvas, SeamOrientation>(nameof(Orientation));
    public static readonly StyledProperty<SeamOperation> OperationProperty =
        AvaloniaProperty.Register<SeamOverlayCanvas, SeamOperation>(nameof(Operation));

    private readonly List<SeamNormalizedPoint> _points = [];
    private bool _drawing;

    static SeamOverlayCanvas()
    {
        AffectsRender<SeamOverlayCanvas>(ImageProperty, CoordinatesProperty, OrientationProperty, OperationProperty);
    }

    public Bitmap? Image { get => GetValue(ImageProperty); set => SetValue(ImageProperty, value); }
    public IReadOnlyList<int>? Coordinates { get => GetValue(CoordinatesProperty); set => SetValue(CoordinatesProperty, value); }
    public SeamOrientation Orientation { get => GetValue(OrientationProperty); set => SetValue(OrientationProperty, value); }
    public SeamOperation Operation { get => GetValue(OperationProperty); set => SetValue(OperationProperty, value); }
    public event EventHandler<IReadOnlyList<SeamNormalizedPoint>>? StrokeCompleted;

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(Brushes.Black, Bounds);
        if (Image is null || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        context.DrawImage(Image, new Rect(Image.Size), Bounds);
        if (Coordinates is null || Coordinates.Count == 0) return;
        var color = Operation == SeamOperation.Remove ? Brushes.Red : Brushes.Cyan;
        var dash = Operation == SeamOperation.Remove ? null : new DashStyle([4d, 3d], 0d);
        var pen = new Pen(color, 2d, dashStyle: dash);
        var geometry = new StreamGeometry();
        using (var sink = geometry.Open())
        {
            for (var main = 0; main < Coordinates.Count; main++)
            {
                var x = Orientation == SeamOrientation.Vertical
                    ? (Coordinates[main] + 0.5d) / Image.PixelSize.Width * Bounds.Width
                    : (main + 0.5d) / Image.PixelSize.Width * Bounds.Width;
                var y = Orientation == SeamOrientation.Vertical
                    ? (main + 0.5d) / Image.PixelSize.Height * Bounds.Height
                    : (Coordinates[main] + 0.5d) / Image.PixelSize.Height * Bounds.Height;
                if (main == 0) sink.BeginFigure(new Point(x, y), false);
                else sink.LineTo(new Point(x, y));
            }
        }
        context.DrawGeometry(null, new Pen(Brushes.Black, 4d), geometry);
        context.DrawGeometry(null, pen, geometry);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || Image is null) return;
        _drawing = true; _points.Clear(); AddPoint(e.GetPosition(this)); e.Pointer.Capture(this); e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drawing) { AddPoint(e.GetPosition(this)); e.Handled = true; }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_drawing) return;
        AddPoint(e.GetPosition(this)); _drawing = false; e.Pointer.Capture(null);
        if (_points.Count != 0) StrokeCompleted?.Invoke(this, _points.ToArray());
        e.Handled = true;
    }

    private void AddPoint(Point point)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var normalized = new SeamNormalizedPoint(Math.Clamp(point.X / Bounds.Width, 0d, 1d),
            Math.Clamp(point.Y / Bounds.Height, 0d, 1d));
        if (_points.Count == 0 || Math.Abs(_points[^1].X - normalized.X) + Math.Abs(_points[^1].Y - normalized.Y) >= 0.002d)
            _points.Add(normalized);
    }
}
