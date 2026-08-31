using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Domain.ColorTransfer;

internal sealed record AggregatedColor(int CellIndex, SrgbColor Srgb, CieLabColor Lab, double Weight, long PixelCount);

/// <summary>把任意尺寸图片压缩为至多 32³ 个确定性颜色单元。</summary>
/// <remarks>
/// 每个 cell 保存实际像素的 Alpha 加权 sRGB 均值，而不是几何中心。固定数组让临时内存不随图片像素数增长；
/// cell 索引 r5&lt;&lt;10 | g5&lt;&lt;5 | b5 同时作为所有并列选择的稳定 tie-break。
/// </remarks>
internal sealed class RgbColorAggregator(SrgbColorSpace srgb, CieLabColorSpace lab)
{
    public const int CellCount = 32 * 32 * 32;

    public IReadOnlyList<AggregatedColor> Aggregate(PixelImage image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        var weights = new double[CellCount]; var red = new double[CellCount];
        var green = new double[CellCount]; var blue = new double[CellCount]; var counts = new int[CellCount];
        var pixels = image.Rgba.Span;
        for (var y = 0; y < image.Size.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < image.Size.Width; x++)
            {
                var offset = ((y * image.Size.Width) + x) * 4; var alpha = pixels[offset + 3];
                if (alpha == 0) continue;
                var cell = ((pixels[offset] >> 3) << 10) | ((pixels[offset + 1] >> 3) << 5) | (pixels[offset + 2] >> 3);
                var weight = alpha / 255d; weights[cell] += weight; counts[cell]++;
                red[cell] += weight * pixels[offset] / 255d;
                green[cell] += weight * pixels[offset + 1] / 255d;
                blue[cell] += weight * pixels[offset + 2] / 255d;
            }
        }
        var result = new List<AggregatedColor>();
        for (var cell = 0; cell < CellCount; cell++)
        {
            if (weights[cell] <= 0d) continue;
            var color = new SrgbColor(red[cell] / weights[cell], green[cell] / weights[cell], blue[cell] / weights[cell]);
            result.Add(new AggregatedColor(cell, color, lab.ToLab(srgb.ToXyz(srgb.Decode(color))), weights[cell], counts[cell]));
        }
        if (result.Count == 0) throw new InvalidOperationException("图片没有可参与主色提取的可见像素。");
        return result;
    }
}
