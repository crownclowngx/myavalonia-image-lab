namespace ImageLabPlugin.Domain.Shared.Imaging;

/// <summary>为只读分析预览生成有界的最近邻副本，防止频谱/差异 UI 再复制一组原尺寸大图。</summary>
internal static class ImagePreviewProjector
{
    public static PixelImage FitWithin(PixelImage source, int maximumDimension = 1024)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (maximumDimension <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDimension));
        }

        var largest = Math.Max(source.Size.Width, source.Size.Height);
        if (largest <= maximumDimension)
        {
            return source.Clone();
        }

        var scale = maximumDimension / (double)largest;
        var targetWidth = Math.Max(1, (int)Math.Round(source.Size.Width * scale));
        var targetHeight = Math.Max(1, (int)Math.Round(source.Size.Height * scale));
        var rgba = new byte[checked(targetWidth * targetHeight * 4)];
        var input = source.Rgba.Span;
        for (var y = 0; y < targetHeight; y++)
        {
            var sourceY = Math.Min(source.Size.Height - 1, (int)(y / scale));
            for (var x = 0; x < targetWidth; x++)
            {
                var sourceX = Math.Min(source.Size.Width - 1, (int)(x / scale));
                var sourceOffset = ((sourceY * source.Size.Width) + sourceX) * 4;
                var targetOffset = ((y * targetWidth) + x) * 4;
                input.Slice(sourceOffset, 4).CopyTo(rgba.AsSpan(targetOffset, 4));
            }
        }

        return new PixelImage(new ImageSize(targetWidth, targetHeight), rgba);
    }
}
