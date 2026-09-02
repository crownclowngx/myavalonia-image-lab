using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ImageLabPlugin.Application.SpectralArt;
using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Infrastructure.Imaging;

/// <summary>通过 Avalonia 当前字体系统把文字离屏栅格为非预乘 RGBA8888。</summary>
/// <remarks>
/// 字体选择和字形栅格是平台事实，因此只能位于 Infrastructure。输出使用透明背景与黑色字形，随后立即进入
/// SpectralPatternNormalizer；Domain 不会看到 FormattedText、Typeface、RenderTargetBitmap 或字体回退对象。
/// 若初始测量超过最大边，适配器按同一比例降低字号并重新测量，不裁掉字形。跨机器字体像素不作为数学协议，
/// 真正参与实验和 recipe 指纹的是栅格后固化的 SpectralPattern。
/// </remarks>
internal sealed class AvaloniaSpectralTextRasterizer : ISpectralTextRasterizer
{
    public Task<PixelImage> RasterizeAsync(
        SpectralTextRasterRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(request.Text)) throw new InvalidDataException("文字不能为空。");
        if (request.Text.Length > 256) throw new InvalidDataException("文字长度不能超过 256 个 UTF-16 单元。");
        if (!double.IsFinite(request.FontSize) || request.FontSize is < 8d or > 256d)
            throw new ArgumentOutOfRangeException(nameof(request), "字号必须位于 8..256。");
        if (request.FontWeight is < 100 or > 900 || request.Padding is < 0 or > 128 ||
            request.MaximumEdge is < 1 or > 512)
            throw new ArgumentOutOfRangeException(nameof(request), "字重、内边距或最大尺寸越界。");
        return Task.FromResult(RasterizeCore(request, cancellationToken));
    }

    private static PixelImage RasterizeCore(
        SpectralTextRasterRequest request,
        CancellationToken cancellationToken)
    {
        var family = string.IsNullOrWhiteSpace(request.FontFamily) ? FontFamily.Default :
            new FontFamily(request.FontFamily);
        var typeface = new Typeface(family, FontStyle.Normal,
            (FontWeight)request.FontWeight, FontStretch.Normal);
        var fontSize = request.FontSize;
        var text = CreateFormattedText(request.Text, typeface, fontSize);
        var rawWidth = Math.Max(1d, text.WidthIncludingTrailingWhitespace + (request.Padding * 2d));
        var rawHeight = Math.Max(1d, text.Height + (request.Padding * 2d));
        var scale = Math.Min(1d, request.MaximumEdge / Math.Max(rawWidth, rawHeight));
        if (scale < 1d)
        {
            fontSize = Math.Max(1d, fontSize * scale);
            text = CreateFormattedText(request.Text, typeface, fontSize);
            rawWidth = text.WidthIncludingTrailingWhitespace + (request.Padding * 2d);
            rawHeight = text.Height + (request.Padding * 2d);
        }
        var width = Math.Clamp((int)Math.Ceiling(rawWidth), 1, request.MaximumEdge);
        var height = Math.Clamp((int)Math.Ceiling(rawHeight), 1, request.MaximumEdge);
        using var target = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96d, 96d));
        using (var drawing = target.CreateDrawingContext())
        {
            cancellationToken.ThrowIfCancellationRequested();
            drawing.DrawText(text, new Point(request.Padding, request.Padding));
        }

        using var writable = new WriteableBitmap(new PixelSize(width, height), new Vector(96d, 96d),
            PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using var framebuffer = writable.Lock();
        target.CopyPixels(framebuffer);
        var rgba = new byte[checked(width * height * 4)];
        var row = new byte[checked(width * 4)];
        for (var y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Marshal.Copy(framebuffer.Address + (y * framebuffer.RowBytes), row, 0, row.Length);
            for (var x = 0; x < width; x++)
            {
                var sourceOffset = x * 4;
                var targetOffset = ((y * width) + x) * 4;
                rgba[targetOffset] = row[sourceOffset + 2];
                rgba[targetOffset + 1] = row[sourceOffset + 1];
                rgba[targetOffset + 2] = row[sourceOffset];
                rgba[targetOffset + 3] = row[sourceOffset + 3];
            }
        }
        if (!rgba.Where((_, index) => (index & 3) == 3).Any(static alpha => alpha != 0))
            throw new InvalidDataException("文字栅格后全透明，请检查字体和文字内容。");
        return new PixelImage(new ImageSize(width, height), rgba);
    }

    private static FormattedText CreateFormattedText(string text, Typeface typeface, double fontSize) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, fontSize, Brushes.Black);
}
