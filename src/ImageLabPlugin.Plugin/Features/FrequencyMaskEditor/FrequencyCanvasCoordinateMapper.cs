using Avalonia;

namespace ImageLabPlugin.Features.FrequencyMaskEditor;

/// <summary>只负责 Uniform/letterbox 控件坐标到归一化显示坐标的映射。</summary>
internal static class FrequencyCanvasCoordinateMapper
{
    public static bool TryMap(Size bounds, PixelSize pixels, Point point, out double normalizedX, out double normalizedY)
    {
        normalizedX = normalizedY = 0d;
        if (!TryGetImageRect(bounds, pixels, out var rect) || !rect.Contains(point)) return false;
        normalizedX = Math.Clamp((point.X - rect.X) / rect.Width, 0d, 1d);
        normalizedY = Math.Clamp((point.Y - rect.Y) / rect.Height, 0d, 1d);
        return true;
    }

    public static bool TryGetImageRect(Size bounds, PixelSize pixels, out Rect rect)
    {
        rect = default;
        if (bounds.Width <= 0d || bounds.Height <= 0d || pixels.Width <= 0 || pixels.Height <= 0) return false;
        var scale = Math.Min(bounds.Width / pixels.Width, bounds.Height / pixels.Height);
        var width = pixels.Width * scale;
        var height = pixels.Height * scale;
        rect = new Rect((bounds.Width - width) / 2d, (bounds.Height - height) / 2d, width, height);
        return true;
    }
}
