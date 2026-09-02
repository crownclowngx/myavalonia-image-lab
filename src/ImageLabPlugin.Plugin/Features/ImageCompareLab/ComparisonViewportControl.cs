using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ImageLabPlugin.Domain.Shared.Analysis;
using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Features.ImageCompareLab;

public enum ComparisonDisplayMode
{
    SideBySide,
    Split,
    Overlay,
    Blink,
    Difference,
    Heatmap
}

/// <summary>集中完成共享视口的源坐标与控件坐标换算。</summary>
/// <remarks>
/// Zoom=0 表示适应面板；Zoom=1 表示代理像素 1:1。中心使用归一化原图坐标，因此窗口大小变化时仍能恢复
/// 同一观察位置。黑边与面板外输入会返回 false，绝不伪造边界像素。
/// </remarks>
internal static class ComparisonViewportMapper
{
    public static bool TryMap(
        Rect bounds,
        PixelSize imageSize,
        Point point,
        double zoom,
        double centerX,
        double centerY,
        ComparisonDisplayMode mode,
        out ImagePoint sourcePoint)
    {
        sourcePoint = default;
        if (bounds.Width <= 0 || bounds.Height <= 0 || imageSize.Width <= 0 || imageSize.Height <= 0) return false;
        var panel = bounds;
        var localPoint = point;
        if (mode == ComparisonDisplayMode.SideBySide)
        {
            var half = bounds.Width / 2d;
            var right = point.X >= half;
            panel = new Rect(0, 0, half, bounds.Height);
            localPoint = new Point(right ? point.X - half : point.X, point.Y);
        }

        var effectiveZoom = zoom <= 0d
            ? Math.Min(panel.Width / imageSize.Width, panel.Height / imageSize.Height)
            : zoom;
        var left = (panel.Width / 2d) - (Math.Clamp(centerX, 0d, 1d) * imageSize.Width * effectiveZoom);
        var top = (panel.Height / 2d) - (Math.Clamp(centerY, 0d, 1d) * imageSize.Height * effectiveZoom);
        var imageRect = new Rect(left, top, imageSize.Width * effectiveZoom, imageSize.Height * effectiveZoom);
        if (!imageRect.Contains(localPoint)) return false;
        var x = Math.Clamp((int)((localPoint.X - imageRect.X) / effectiveZoom), 0, imageSize.Width - 1);
        var y = Math.Clamp((int)((localPoint.Y - imageRect.Y) / effectiveZoom), 0, imageSize.Height - 1);
        sourcePoint = new ImagePoint(x, y);
        return true;
    }
}

/// <summary>只负责绘制已准备好的 Bitmap、裁剪和共享变换，不读取文件或执行比较算法。</summary>
public sealed class ComparisonViewportControl : Control
{
    public static readonly StyledProperty<Bitmap?> ReferenceImageProperty =
        AvaloniaProperty.Register<ComparisonViewportControl, Bitmap?>(nameof(ReferenceImage));
    public static readonly StyledProperty<Bitmap?> CandidateImageProperty =
        AvaloniaProperty.Register<ComparisonViewportControl, Bitmap?>(nameof(CandidateImage));
    public static readonly StyledProperty<Bitmap?> ProjectionImageProperty =
        AvaloniaProperty.Register<ComparisonViewportControl, Bitmap?>(nameof(ProjectionImage));
    public static readonly StyledProperty<ComparisonDisplayMode> ModeProperty =
        AvaloniaProperty.Register<ComparisonViewportControl, ComparisonDisplayMode>(nameof(Mode));
    public static readonly StyledProperty<double> SplitRatioProperty =
        AvaloniaProperty.Register<ComparisonViewportControl, double>(nameof(SplitRatio), 0.5d);
    public static readonly StyledProperty<double> OverlayOpacityProperty =
        AvaloniaProperty.Register<ComparisonViewportControl, double>(nameof(OverlayOpacity), 0.5d);
    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<ComparisonViewportControl, double>(nameof(Zoom), 0d);
    public static readonly StyledProperty<double> CenterXProperty =
        AvaloniaProperty.Register<ComparisonViewportControl, double>(nameof(CenterX), 0.5d);
    public static readonly StyledProperty<double> CenterYProperty =
        AvaloniaProperty.Register<ComparisonViewportControl, double>(nameof(CenterY), 0.5d);
    public static readonly StyledProperty<bool> ShowCandidateBlinkFrameProperty =
        AvaloniaProperty.Register<ComparisonViewportControl, bool>(nameof(ShowCandidateBlinkFrame));
    public static readonly StyledProperty<bool> ShowCrosshairProperty =
        AvaloniaProperty.Register<ComparisonViewportControl, bool>(nameof(ShowCrosshair), true);
    public static readonly StyledProperty<int> SelectedXProperty =
        AvaloniaProperty.Register<ComparisonViewportControl, int>(nameof(SelectedX));
    public static readonly StyledProperty<int> SelectedYProperty =
        AvaloniaProperty.Register<ComparisonViewportControl, int>(nameof(SelectedY));

    static ComparisonViewportControl()
    {
        AffectsRender<ComparisonViewportControl>(ReferenceImageProperty, CandidateImageProperty, ProjectionImageProperty,
            ModeProperty, SplitRatioProperty, OverlayOpacityProperty, ZoomProperty, CenterXProperty, CenterYProperty,
            ShowCandidateBlinkFrameProperty, ShowCrosshairProperty, SelectedXProperty, SelectedYProperty);
        FocusableProperty.OverrideDefaultValue<ComparisonViewportControl>(true);
    }

    public Bitmap? ReferenceImage { get => GetValue(ReferenceImageProperty); set => SetValue(ReferenceImageProperty, value); }
    public Bitmap? CandidateImage { get => GetValue(CandidateImageProperty); set => SetValue(CandidateImageProperty, value); }
    public Bitmap? ProjectionImage { get => GetValue(ProjectionImageProperty); set => SetValue(ProjectionImageProperty, value); }
    public ComparisonDisplayMode Mode { get => GetValue(ModeProperty); set => SetValue(ModeProperty, value); }
    public double SplitRatio { get => GetValue(SplitRatioProperty); set => SetValue(SplitRatioProperty, value); }
    public double OverlayOpacity { get => GetValue(OverlayOpacityProperty); set => SetValue(OverlayOpacityProperty, value); }
    public double Zoom { get => GetValue(ZoomProperty); set => SetValue(ZoomProperty, value); }
    public double CenterX { get => GetValue(CenterXProperty); set => SetValue(CenterXProperty, value); }
    public double CenterY { get => GetValue(CenterYProperty); set => SetValue(CenterYProperty, value); }
    public bool ShowCandidateBlinkFrame { get => GetValue(ShowCandidateBlinkFrameProperty); set => SetValue(ShowCandidateBlinkFrameProperty, value); }
    public bool ShowCrosshair { get => GetValue(ShowCrosshairProperty); set => SetValue(ShowCrosshairProperty, value); }
    public int SelectedX { get => GetValue(SelectedXProperty); set => SetValue(SelectedXProperty, value); }
    public int SelectedY { get => GetValue(SelectedYProperty); set => SetValue(SelectedYProperty, value); }

    internal bool TryMap(Point point, out ImagePoint sourcePoint)
    {
        sourcePoint = default;
        var bitmap = ReferenceImage ?? CandidateImage ?? ProjectionImage;
        return bitmap is not null && ComparisonViewportMapper.TryMap(Bounds, bitmap.PixelSize, point, Zoom, CenterX, CenterY, Mode, out sourcePoint);
    }

    /// <summary>计算缩放后仍让指针下代理坐标保持不动的新归一化中心。</summary>
    internal bool TryCalculateAnchoredCenter(Point point, double newZoom, out double centerX, out double centerY)
    {
        centerX = CenterX; centerY = CenterY;
        var bitmap = ReferenceImage ?? CandidateImage ?? ProjectionImage;
        if (bitmap is null || newZoom <= 0d) return false;
        var panel = Bounds; var local = point;
        if (Mode == ComparisonDisplayMode.SideBySide)
        {
            var half = Bounds.Width / 2d; var right = point.X >= half;
            panel = new Rect(0, 0, half, Bounds.Height); local = new Point(right ? point.X - half : point.X, point.Y);
        }
        if (panel.Width <= 0d || panel.Height <= 0d) return false;
        var oldZoom = Zoom <= 0d
            ? Math.Min(panel.Width / bitmap.PixelSize.Width, panel.Height / bitmap.PixelSize.Height)
            : Zoom;
        var sourceX = (CenterX * bitmap.PixelSize.Width) + ((local.X - (panel.Width / 2d)) / oldZoom);
        var sourceY = (CenterY * bitmap.PixelSize.Height) + ((local.Y - (panel.Height / 2d)) / oldZoom);
        centerX = Math.Clamp((sourceX - ((local.X - (panel.Width / 2d)) / newZoom)) / bitmap.PixelSize.Width, 0d, 1d);
        centerY = Math.Clamp((sourceY - ((local.Y - (panel.Height / 2d)) / newZoom)) / bitmap.PixelSize.Height, 0d, 1d);
        return true;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(Brushes.Black, Bounds);
        if (Mode == ComparisonDisplayMode.SideBySide)
        {
            var half = Bounds.Width / 2d;
            DrawBitmap(context, ReferenceImage, new Rect(0, 0, half, Bounds.Height));
            using (context.PushTransform(Matrix.CreateTranslation(half, 0)))
                DrawBitmap(context, CandidateImage, new Rect(0, 0, half, Bounds.Height));
            context.DrawLine(new Pen(Brushes.Gray, 1), new Point(half, 0), new Point(half, Bounds.Height));
            return;
        }

        if (Mode is ComparisonDisplayMode.Difference or ComparisonDisplayMode.Heatmap)
        {
            DrawBitmap(context, ProjectionImage, Bounds); return;
        }
        DrawBitmap(context, ReferenceImage, Bounds);
        if (Mode == ComparisonDisplayMode.Split)
        {
            var split = Math.Clamp(SplitRatio, 0d, 1d) * Bounds.Width;
            using (context.PushClip(new Rect(split, 0, Bounds.Width - split, Bounds.Height))) DrawBitmap(context, CandidateImage, Bounds);
            context.DrawLine(new Pen(Brushes.White, 2), new Point(split, 0), new Point(split, Bounds.Height));
        }
        else if (Mode == ComparisonDisplayMode.Overlay)
        {
            using (context.PushOpacity(Math.Clamp(OverlayOpacity, 0d, 1d))) DrawBitmap(context, CandidateImage, Bounds);
        }
        else if (Mode == ComparisonDisplayMode.Blink && ShowCandidateBlinkFrame) DrawBitmap(context, CandidateImage, Bounds);
    }

    private void DrawBitmap(DrawingContext context, Bitmap? bitmap, Rect panel)
    {
        if (bitmap is null) return;
        var effectiveZoom = Zoom <= 0d
            ? Math.Min(panel.Width / bitmap.PixelSize.Width, panel.Height / bitmap.PixelSize.Height)
            : Zoom;
        var destination = new Rect(
            panel.X + (panel.Width / 2d) - (CenterX * bitmap.PixelSize.Width * effectiveZoom),
            panel.Y + (panel.Height / 2d) - (CenterY * bitmap.PixelSize.Height * effectiveZoom),
            bitmap.PixelSize.Width * effectiveZoom,
            bitmap.PixelSize.Height * effectiveZoom);
        context.DrawImage(bitmap, new Rect(bitmap.Size), destination);
        if (!ShowCrosshair || SelectedX < 0 || SelectedY < 0) return;
        var crossX = destination.X + ((SelectedX + 0.5d) * effectiveZoom);
        var crossY = destination.Y + ((SelectedY + 0.5d) * effectiveZoom);
        context.DrawLine(new Pen(Brushes.White, 1), new Point(crossX - 8, crossY), new Point(crossX + 8, crossY));
        context.DrawLine(new Pen(Brushes.White, 1), new Point(crossX, crossY - 8), new Point(crossX, crossY + 8));
    }
}
