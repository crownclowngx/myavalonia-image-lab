using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Domain.ColorTransfer;

internal sealed record ColorPixelFact(int X, int Y, byte Red, byte Green, byte Blue, byte Alpha,
    HsvColor Hsv, CieLabColor Lab, int? PaletteClusterIndex, double? DeltaE76);

/// <summary>按一张图片自己的坐标返回 sRGB/HSV/Lab 与调色板归属事实。</summary>
/// <remarks>
/// 目标和参考允许不同尺寸，因此调用者分别提供坐标，不能偷偷把坐标比例映射为像素配对。
/// A=0 的隐藏 RGB 仍可作为探针事实显示，但不会声称参与统计或调色板分配。
/// </remarks>
internal sealed class ColorPixelInspector(SrgbColorSpace srgb, CieLabColorSpace lab, HsvColorSpace hsv, CieDeltaE deltaE)
{
    public ColorPixelFact Inspect(PixelImage image, int x, int y, FrozenPalette? palette)
    {
        ArgumentNullException.ThrowIfNull(image); var pixel = image.GetPixel(x, y);
        var color = SrgbColor.FromBytes(pixel.R, pixel.G, pixel.B);
        var labColor = lab.ToLab(srgb.ToXyz(srgb.Decode(color))); var hsvColor = hsv.ToHsv(color);
        if (pixel.A == 0 || palette is null || palette.Entries.Count == 0)
            return new ColorPixelFact(x, y, pixel.R, pixel.G, pixel.B, pixel.A, hsvColor, labColor, null, null);
        var selected = palette.Entries[0]; var best = deltaE.DeltaE76(labColor, selected.Lab);
        foreach (var entry in palette.Entries.OrderBy(item => item.ClusterIndex).Skip(1))
        { var value = deltaE.DeltaE76(labColor, entry.Lab); if (value < best) { selected = entry; best = value; } }
        return new ColorPixelFact(x, y, pixel.R, pixel.G, pixel.B, pixel.A, hsvColor, labColor, selected.ClusterIndex, best);
    }
}
