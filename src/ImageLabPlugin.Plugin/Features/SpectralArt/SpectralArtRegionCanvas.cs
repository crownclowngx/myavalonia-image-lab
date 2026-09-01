using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ImageLabPlugin.Features.Common;

namespace ImageLabPlugin.Features.SpectralArt;

internal sealed class SpectralRegionChangedEventArgs(double left, double top, double right, double bottom) : EventArgs
{
    public double Left { get; } = left;
    public double Top { get; } = top;
    public double Right { get; } = right;
    public double Bottom { get; } = bottom;
}

/// <summary>在 Uniform/letterbox 频谱上编辑主矩形，并绘制共轭副本与禁止区。</summary>
/// <remarks>
/// 控件只处理显示坐标、手柄命中、Pointer capture 和键盘移动；闭开边界、偶数取整、面积限制、DC/Nyquist
/// 等最终合法性仍由领域 SpectralPatternMapper 统一判定，UI 预览不能替代数值门禁。
/// </remarks>
internal sealed class SpectralArtRegionCanvas : Control
{
    private enum DragPart { None, Move, TopLeft, TopRight, BottomLeft, BottomRight }
    private DragPart _dragPart;
    private Point _last;

    public static readonly StyledProperty<Bitmap?> SpectrumProperty = AvaloniaProperty.Register<SpectralArtRegionCanvas, Bitmap?>(nameof(Spectrum));
    public static readonly StyledProperty<Bitmap?> MappingProperty = AvaloniaProperty.Register<SpectralArtRegionCanvas, Bitmap?>(nameof(Mapping));
    public static readonly StyledProperty<double> LeftFrequencyProperty = AvaloniaProperty.Register<SpectralArtRegionCanvas, double>(nameof(LeftFrequency), 0.14d);
    public static readonly StyledProperty<double> TopFrequencyProperty = AvaloniaProperty.Register<SpectralArtRegionCanvas, double>(nameof(TopFrequency), -0.34d);
    public static readonly StyledProperty<double> RightFrequencyProperty = AvaloniaProperty.Register<SpectralArtRegionCanvas, double>(nameof(RightFrequency), 0.34d);
    public static readonly StyledProperty<double> BottomFrequencyProperty = AvaloniaProperty.Register<SpectralArtRegionCanvas, double>(nameof(BottomFrequency), -0.14d);

    static SpectralArtRegionCanvas()
    {
        AffectsRender<SpectralArtRegionCanvas>(SpectrumProperty, MappingProperty, LeftFrequencyProperty,
            TopFrequencyProperty, RightFrequencyProperty, BottomFrequencyProperty);
        FocusableProperty.OverrideDefaultValue<SpectralArtRegionCanvas>(true);
    }

    public Bitmap? Spectrum { get => GetValue(SpectrumProperty); set => SetValue(SpectrumProperty, value); }
    public Bitmap? Mapping { get => GetValue(MappingProperty); set => SetValue(MappingProperty, value); }
    public double LeftFrequency { get => GetValue(LeftFrequencyProperty); set => SetValue(LeftFrequencyProperty, value); }
    public double TopFrequency { get => GetValue(TopFrequencyProperty); set => SetValue(TopFrequencyProperty, value); }
    public double RightFrequency { get => GetValue(RightFrequencyProperty); set => SetValue(RightFrequencyProperty, value); }
    public double BottomFrequency { get => GetValue(BottomFrequencyProperty); set => SetValue(BottomFrequencyProperty, value); }
    public event EventHandler<SpectralRegionChangedEventArgs>? RegionChanged;

    public override void Render(DrawingContext context)
    {
        base.Render(context); context.FillRectangle(Brushes.Black, Bounds);
        var image = Spectrum ?? Mapping;
        if (image is null || !UniformImageCoordinateMapper.TryGetImageRect(Bounds.Size, image.PixelSize, out var target)) return;
        if (Spectrum is not null) context.DrawImage(Spectrum, new Rect(Spectrum.Size), target);
        if (Mapping is not null) using (context.PushOpacity(0.48d)) context.DrawImage(Mapping, new Rect(Mapping.Size), target);
        context.DrawRectangle(new Pen(Brushes.Gray, 1), target);
        // DC 圆与水平/垂直轴带只用于解释禁止区，最终判断由领域验证器完成。
        var center = new Point(target.Center.X, target.Center.Y); var radius = Math.Min(target.Width, target.Height) * 0.08d;
        context.DrawEllipse(null, new Pen(Brushes.OrangeRed, 1.5d), center, radius, radius);
        context.DrawLine(new Pen(Brushes.OrangeRed, 1d), new Point(target.X, center.Y), new Point(target.Right, center.Y));
        context.DrawLine(new Pen(Brushes.OrangeRed, 1d), new Point(center.X, target.Y), new Point(center.X, target.Bottom));
        DrawRegion(context, target, LeftFrequency, TopFrequency, RightFrequency, BottomFrequency, Brushes.Lime, true);
        DrawRegion(context, target, -RightFrequency, -BottomFrequency, -LeftFrequency, -TopFrequency, Brushes.Cyan, false);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e); if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var point = e.GetPosition(this); if (!TryImageRect(out var imageRect) || !imageRect.Contains(point)) return;
        _dragPart = HitTest(imageRect, point); if (_dragPart == DragPart.None) return;
        _last = point; e.Pointer.Capture(this); Focus(); e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e); if (_dragPart == DragPart.None || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || !TryImageRect(out var rect)) return;
        var point = e.GetPosition(this); var dx = (point.X - _last.X) / rect.Width; var dy = (point.Y - _last.Y) / rect.Height; _last = point;
        ApplyDrag(dx, dy); e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    { base.OnPointerReleased(e); if (_dragPart == DragPart.None) return; _dragPart = DragPart.None; e.Pointer.Capture(null); e.Handled = true; }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { base.OnPointerCaptureLost(e); _dragPart = DragPart.None; }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e); var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 0.02d : 0.005d;
        var dx = e.Key switch { Key.Left => -step, Key.Right => step, _ => 0d };
        var dy = e.Key switch { Key.Up => -step, Key.Down => step, _ => 0d };
        if (dx == 0d && dy == 0d) return; Move(dx, dy); e.Handled = true;
    }

    private void ApplyDrag(double dx, double dy)
    {
        var left = LeftFrequency; var top = TopFrequency; var right = RightFrequency; var bottom = BottomFrequency;
        switch (_dragPart)
        {
            case DragPart.Move: left += dx; right += dx; top += dy; bottom += dy; break;
            case DragPart.TopLeft: left += dx; top += dy; break;
            case DragPart.TopRight: right += dx; top += dy; break;
            case DragPart.BottomLeft: left += dx; bottom += dy; break;
            case DragPart.BottomRight: right += dx; bottom += dy; break;
        }
        Commit(left, top, right, bottom);
    }

    private void Move(double dx, double dy) => Commit(LeftFrequency + dx, TopFrequency + dy, RightFrequency + dx, BottomFrequency + dy);

    private void Commit(double left, double top, double right, double bottom)
    {
        var width = right - left; var height = bottom - top;
        left = Math.Clamp(left, -0.5d, 0.5d - width); top = Math.Clamp(top, -0.5d, 0.5d - height);
        right = Math.Clamp(Math.Max(left + 0.005d, right), -0.5d, 0.5d); bottom = Math.Clamp(Math.Max(top + 0.005d, bottom), -0.5d, 0.5d);
        LeftFrequency = left; TopFrequency = top; RightFrequency = right; BottomFrequency = bottom;
        RegionChanged?.Invoke(this, new(left, top, right, bottom)); InvalidateVisual();
    }

    private DragPart HitTest(Rect target, Point point)
    {
        var rect = ToRect(target, LeftFrequency, TopFrequency, RightFrequency, BottomFrequency);
        var corners = new[] { rect.TopLeft, rect.TopRight, rect.BottomLeft, rect.BottomRight };
        for (var i = 0; i < corners.Length; i++)
            if (Math.Sqrt(Math.Pow(corners[i].X - point.X, 2d) + Math.Pow(corners[i].Y - point.Y, 2d)) <= 10d)
                return (DragPart)(i + 2);
        return rect.Contains(point) ? DragPart.Move : DragPart.None;
    }

    private bool TryImageRect(out Rect rect)
    { rect = default; var image = Spectrum ?? Mapping; return image is not null && UniformImageCoordinateMapper.TryGetImageRect(Bounds.Size, image.PixelSize, out rect); }

    private static void DrawRegion(DrawingContext context, Rect target, double left, double top, double right, double bottom, IBrush brush, bool handles)
    {
        var rect = ToRect(target, left, top, right, bottom); var pen = new Pen(brush, handles ? 2d : 1.25d); context.DrawRectangle(pen, rect);
        if (!handles) return; foreach (var point in new[] { rect.TopLeft, rect.TopRight, rect.BottomLeft, rect.BottomRight }) context.DrawRectangle(brush, null, new Rect(point.X - 4, point.Y - 4, 8, 8));
    }

    private static Rect ToRect(Rect target, double left, double top, double right, double bottom) =>
        new(target.X + ((left + 0.5d) * target.Width), target.Y + ((top + 0.5d) * target.Height),
            (right - left) * target.Width, (bottom - top) * target.Height);
}
