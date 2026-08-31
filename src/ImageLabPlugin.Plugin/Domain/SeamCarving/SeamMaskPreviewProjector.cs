using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Domain.SeamCarving;

/// <summary>生成仅供 UI 观察的区域纹理叠加，不修改算法工作图或蒙版。</summary>
/// <remarks>
/// 保护区使用洋红斜纹，优先删除区使用黄绿反向斜纹；颜色之外还有不同纹理，避免只靠红绿区分。
/// 透明源像素上的标记也强制可见，但输出只用于预览，绝不进入能量、路径、导出或质量比较。
/// </remarks>
internal sealed class SeamMaskPreviewProjector
{
    public PixelImage Project(PixelImage image, SeamMask mask, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image); ArgumentNullException.ThrowIfNull(mask);
        if (image.Size != mask.Size) throw new ArgumentException("图片与蒙版尺寸必须一致。", nameof(mask));
        var result = image.Rgba.ToArray(); var values = mask.Values.Span;
        for (var y = 0; y < image.Size.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < image.Size.Width; x++)
            {
                var index = (y * image.Size.Width) + x;
                var value = (SeamMaskValue)values[index];
                if (value == SeamMaskValue.Normal) continue;
                var offset = index * 4;
                var texture = value == SeamMaskValue.Protect ? (x + y) % 8 < 2 : (x - y + 8_000_000) % 8 < 2;
                var amount = texture ? 0.70d : 0.32d;
                var (red, green, blue) = value == SeamMaskValue.Protect
                    ? (255d, 0d, 220d) : (190d, 255d, 0d);
                result[offset] = Blend(result[offset], red, amount);
                result[offset + 1] = Blend(result[offset + 1], green, amount);
                result[offset + 2] = Blend(result[offset + 2], blue, amount);
                result[offset + 3] = Math.Max(result[offset + 3], (byte)180);
            }
        }
        return new PixelImage(image.Size, result);
    }

    private static byte Blend(byte source, double overlay, double amount) => SeamInserter.ToByte(
        (source * (1d - amount)) + (overlay * amount));
}
