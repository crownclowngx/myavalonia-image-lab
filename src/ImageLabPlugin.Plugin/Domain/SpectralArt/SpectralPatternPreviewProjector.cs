using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Domain.SpectralArt;

/// <summary>把不可变 Pattern 权重投影为不参与数值反馈的灰度 RGBA 预览。</summary>
/// <remarks>
/// 投影集中在领域表现事实中，避免 Document 为创建 Bitmap 而持有像素循环。每个权重按 ToEven 映射到
/// 0..255，Alpha 固定 255；该图只用于显示，映射和幅度写入始终读取原始 double 权重。
/// </remarks>
internal sealed class SpectralPatternPreviewProjector
{
    public PixelImage Project(SpectralPattern pattern, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        var rgba = new byte[checked(pattern.Width * pattern.Height * 4)];
        for (var y = 0; y < pattern.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < pattern.Width; x++)
            {
                var value = (byte)Math.Clamp((int)Math.Round(pattern[x, y] * 255d,
                    MidpointRounding.ToEven), 0, 255);
                var offset = ((y * pattern.Width) + x) * 4;
                rgba[offset] = rgba[offset + 1] = rgba[offset + 2] = value;
                rgba[offset + 3] = 255;
            }
        }
        return new PixelImage(new ImageSize(pattern.Width, pattern.Height), rgba);
    }
}
