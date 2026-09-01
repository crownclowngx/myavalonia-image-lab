using Avalonia;
using ImageLabPlugin.Features.Common;

namespace ImageLabPlugin.Features.FrequencyMaskEditor;

/// <summary>只负责 Uniform/letterbox 控件坐标到归一化显示坐标的映射。</summary>
internal static class FrequencyCanvasCoordinateMapper
{
    public static bool TryMap(Size bounds, PixelSize pixels, Point point, out double normalizedX, out double normalizedY)
    {
        return UniformImageCoordinateMapper.TryMap(bounds, pixels, point, out normalizedX, out normalizedY);
    }

    public static bool TryGetImageRect(Size bounds, PixelSize pixels, out Rect rect)
    {
        return UniformImageCoordinateMapper.TryGetImageRect(bounds, pixels, out rect);
    }
}
