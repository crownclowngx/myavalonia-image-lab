using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Domain.ImageOscilloscope;

/// <summary>把可丢弃密度投影着色为 UI 代理图，不接触精确计数和源像素。</summary>
/// <remarks>
/// 这是显示适配而非分析算法：主题色只影响 RGBA，不影响 tone、P99.5 上限、坐标或 generation。
/// RGB Parade 在此横向拼成带固定间隔的三段，领域仍保留三份独立同尺寸计数栅格。
/// </remarks>
internal sealed class ImageOscilloscopeRasterizer
{
    public PixelImage Rasterize(ScopeDensityProjection projection, byte red, byte green, byte blue,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projection);
        var rgba = new byte[checked(projection.Width * projection.Height * 4)];
        for (var index = 0; index < projection.Tones.Count; index++)
        {
            if ((index & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            var tone = projection.Tones[index];
            var offset = index * 4;
            rgba[offset] = (byte)Math.Round(red * tone, MidpointRounding.ToEven);
            rgba[offset + 1] = (byte)Math.Round(green * tone, MidpointRounding.ToEven);
            rgba[offset + 2] = (byte)Math.Round(blue * tone, MidpointRounding.ToEven);
            rgba[offset + 3] = 255;
        }
        return new PixelImage(new ImageSize(projection.Width, projection.Height), rgba);
    }

    public PixelImage RasterizeParade(ScopeDensityProjection red, ScopeDensityProjection green,
        ScopeDensityProjection blue, CancellationToken cancellationToken = default)
    {
        if (red.Width != green.Width || red.Width != blue.Width || red.Height != green.Height || red.Height != blue.Height)
            throw new ArgumentException("Parade 投影必须同尺寸。");
        const int gap = 4;
        var width = checked((red.Width * 3) + (gap * 2));
        var rgba = new byte[checked(width * red.Height * 4)];
        PaintSegment(red, rgba, width, 0, 255, 88, 88, cancellationToken);
        PaintSegment(green, rgba, width, red.Width + gap, 72, 230, 112, cancellationToken);
        PaintSegment(blue, rgba, width, (red.Width + gap) * 2, 82, 152, 255, cancellationToken);
        return new PixelImage(new ImageSize(width, red.Height), rgba);
    }

    private static void PaintSegment(ScopeDensityProjection projection, Span<byte> destination, int destinationWidth,
        int left, byte red, byte green, byte blue, CancellationToken cancellationToken)
    {
        for (var y = 0; y < projection.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < projection.Width; x++)
            {
                var tone = projection.Tones[(y * projection.Width) + x];
                var offset = ((y * destinationWidth) + left + x) * 4;
                destination[offset] = (byte)Math.Round(red * tone, MidpointRounding.ToEven);
                destination[offset + 1] = (byte)Math.Round(green * tone, MidpointRounding.ToEven);
                destination[offset + 2] = (byte)Math.Round(blue * tone, MidpointRounding.ToEven);
                destination[offset + 3] = 255;
            }
        }
    }
}
