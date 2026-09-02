namespace ImageLabPlugin.Features.MagnitudePhaseSwap;

/// <summary>把含 letterbox 的视图坐标映射到同频点归一化坐标。</summary>
internal static class MagnitudePhaseCoordinateMapper
{
    public static (double X, double Y, bool IsInside) ToNormalized(double x, double y,
        double viewWidth, double viewHeight, int imageWidth, int imageHeight)
    {
        if (viewWidth <= 0d || viewHeight <= 0d || imageWidth <= 0 || imageHeight <= 0) return (0d, 0d, false);
        var scale = Math.Min(viewWidth / imageWidth, viewHeight / imageHeight);
        var width = imageWidth * scale; var height = imageHeight * scale;
        var left = (viewWidth - width) / 2d; var top = (viewHeight - height) / 2d;
        var inside = x >= left && y >= top && x < left + width && y < top + height;
        return (Math.Clamp((x - left) / width, 0d, 1d), Math.Clamp((y - top) / height, 0d, 1d), inside);
    }
}
