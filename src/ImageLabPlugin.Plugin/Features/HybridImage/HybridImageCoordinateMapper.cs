namespace ImageLabPlugin.Features.HybridImage;

/// <summary>在 letterbox 画布与归一化图片坐标之间转换，不参与相似变换或像素采样。</summary>
internal static class HybridImageCoordinateMapper
{
    public static (double X, double Y) ToNormalized(double pointerX, double pointerY,
        double viewportWidth, double viewportHeight, int imageWidth, int imageHeight)
    {
        if (viewportWidth <= 0d || viewportHeight <= 0d || imageWidth <= 0 || imageHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(viewportWidth));
        var scale = Math.Min(viewportWidth / imageWidth, viewportHeight / imageHeight);
        var renderedWidth = imageWidth * scale;
        var renderedHeight = imageHeight * scale;
        var left = (viewportWidth - renderedWidth) / 2d;
        var top = (viewportHeight - renderedHeight) / 2d;
        return (Math.Clamp((pointerX - left) / renderedWidth, 0d, 1d),
            Math.Clamp((pointerY - top) / renderedHeight, 0d, 1d));
    }
}
