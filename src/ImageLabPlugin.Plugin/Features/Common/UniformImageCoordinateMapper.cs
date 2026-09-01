using Avalonia;

namespace ImageLabPlugin.Features.Common;

/// <summary>只负责 Uniform/letterbox 图片矩形与归一化显示坐标之间的转换。</summary>
/// <remarks>
/// 该类型属于表现层共享几何：它不知道 FFT、共轭、Pattern、遮罩或手势含义。不同控件可复用完全相同的
/// letterbox 边界，同时把“点击代表什么”留在各自 Feature 中，避免跨 Feature 依赖业务控件。
/// </remarks>
internal static class UniformImageCoordinateMapper
{
    public static bool TryMap(
        Size bounds,
        PixelSize pixels,
        Point point,
        out double normalizedX,
        out double normalizedY)
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
        if (bounds.Width <= 0d || bounds.Height <= 0d || pixels.Width <= 0 || pixels.Height <= 0)
            return false;
        var scale = Math.Min(bounds.Width / pixels.Width, bounds.Height / pixels.Height);
        var width = pixels.Width * scale;
        var height = pixels.Height * scale;
        rect = new Rect((bounds.Width - width) / 2d, (bounds.Height - height) / 2d, width, height);
        return true;
    }
}
