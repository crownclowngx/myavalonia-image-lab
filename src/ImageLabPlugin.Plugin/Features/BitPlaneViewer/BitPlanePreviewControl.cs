using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace ImageLabPlugin.Features.BitPlaneViewer;

/// <summary>绘制棋盘背景、Uniform 图片和统一的源坐标点击映射。</summary>
/// <remarks>
/// 控件只负责显示坐标，不读取领域通道。棋盘格用于说明 Alpha 重建后的透明效果；单位平面 Bitmap
/// 自身仍保持 Alpha=255。有效绘制矩形以外的留白不会被误映射为边缘像素。
/// </remarks>
public sealed class BitPlanePreviewControl : Control
{
    public static readonly StyledProperty<Bitmap?> SourceProperty =
        AvaloniaProperty.Register<BitPlanePreviewControl, Bitmap?>(nameof(Source));
    public static readonly StyledProperty<bool> ShowCheckerboardProperty =
        AvaloniaProperty.Register<BitPlanePreviewControl, bool>(nameof(ShowCheckerboard));

    static BitPlanePreviewControl()
    {
        AffectsRender<BitPlanePreviewControl>(SourceProperty, ShowCheckerboardProperty);
    }

    public Bitmap? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public bool ShowCheckerboard { get => GetValue(ShowCheckerboardProperty); set => SetValue(ShowCheckerboardProperty, value); }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (ShowCheckerboard) DrawCheckerboard(context, Bounds.Size);
        if (Source is null || !TryGetImageRect(Bounds.Size, Source.PixelSize, out var destination)) return;
        context.DrawImage(Source, new Rect(Source.Size), destination);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Source is null || DataContext is not BitPlaneViewerDocument document ||
            !TryMap(Bounds.Size, Source.PixelSize, e.GetPosition(this), out var x, out var y)) return;
        document.InspectAtNormalized(x, y);
        e.Handled = true;
    }

    internal static bool TryMap(Size bounds, PixelSize pixels, Point point, out double normalizedX, out double normalizedY)
    {
        normalizedX = normalizedY = 0d;
        if (!TryGetImageRect(bounds, pixels, out var rect) || !rect.Contains(point)) return false;
        normalizedX = Math.Clamp((point.X - rect.X) / rect.Width, 0d, Math.BitDecrement(1d));
        normalizedY = Math.Clamp((point.Y - rect.Y) / rect.Height, 0d, Math.BitDecrement(1d));
        return true;
    }

    private static bool TryGetImageRect(Size bounds, PixelSize pixels, out Rect rect)
    {
        rect = default;
        if (bounds.Width <= 0d || bounds.Height <= 0d || pixels.Width <= 0 || pixels.Height <= 0) return false;
        var scale = Math.Min(bounds.Width / pixels.Width, bounds.Height / pixels.Height);
        var width = pixels.Width * scale;
        var height = pixels.Height * scale;
        rect = new Rect((bounds.Width - width) / 2d, (bounds.Height - height) / 2d, width, height);
        return true;
    }

    private static void DrawCheckerboard(DrawingContext context, Size size)
    {
        const double cell = 12d;
        for (var y = 0d; y < size.Height; y += cell)
        for (var x = 0d; x < size.Width; x += cell)
        {
            var even = (((int)(x / cell)) + ((int)(y / cell))) % 2 == 0;
            context.FillRectangle(even ? Brushes.White : Brushes.LightGray, new Rect(x, y, cell, cell));
        }
    }
}
