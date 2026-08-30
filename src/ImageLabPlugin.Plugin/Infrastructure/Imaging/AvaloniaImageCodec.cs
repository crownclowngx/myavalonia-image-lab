using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Infrastructure.Imaging;

/// <summary>使用 Host 已共享的 Avalonia 图片后端完成 PNG/JPEG 编解码。</summary>
/// <remarks>
/// 适配器把 BGRA 平台像素立即复制成领域 RGBA，平台 Bitmap 不会越过方法边界。这样 Domain 和算法测试
/// 不依赖 Skia，也避免插件私带另一份原生图片运行库与 Host 冲突。
/// </remarks>
internal sealed class AvaloniaImageCodec : IImageCodec
{
    /// <summary>限制压缩文件本身，避免在交给平台解码器前把异常大文件整体读入内存。</summary>
    public const int MaximumEncodedBytes = 64 * 1024 * 1024;

    public async Task<PixelImage> DecodeAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fileLength = new FileInfo(path).Length;
        if (fileLength > MaximumEncodedBytes)
        {
            throw new InvalidDataException($"图片文件超过 V1 的 {MaximumEncodedBytes / 1024 / 1024} MiB 编码大小上限。");
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return await DecodeAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    public Task<PixelImage> DecodeAsync(ReadOnlyMemory<byte> encodedImage, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (encodedImage.Length > MaximumEncodedBytes)
        {
            throw new InvalidDataException($"图片数据超过 V1 的 {MaximumEncodedBytes / 1024 / 1024} MiB 编码大小上限。");
        }

        using var stream = new MemoryStream(encodedImage.ToArray(), writable: false);
        using var source = new Bitmap(stream);
        var size = new ImageSize(source.PixelSize.Width, source.PixelSize.Height);
        using var writable = new WriteableBitmap(
            source.PixelSize,
            new Vector(96d, 96d),
            PixelFormat.Bgra8888,
            AlphaFormat.Unpremul);
        using var framebuffer = writable.Lock();
        source.CopyPixels(framebuffer);

        var rgba = new byte[checked((int)(size.PixelCount * 4))];
        var row = new byte[checked(size.Width * 4)];
        for (var y = 0; y < size.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Marshal.Copy(framebuffer.Address + (y * framebuffer.RowBytes), row, 0, row.Length);
            for (var x = 0; x < size.Width; x++)
            {
                var sourceOffset = x * 4;
                var targetOffset = ((y * size.Width) + x) * 4;
                rgba[targetOffset] = row[sourceOffset + 2];
                rgba[targetOffset + 1] = row[sourceOffset + 1];
                rgba[targetOffset + 2] = row[sourceOffset];
                rgba[targetOffset + 3] = row[sourceOffset + 3];
            }
        }

        return Task.FromResult(new PixelImage(size, rgba));
    }

    public Task<byte[]> EncodeAsync(
        PixelImage image,
        ImageOutputFormat format,
        int jpegQuality,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (jpegQuality is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(jpegQuality), jpegQuality, "JPEG 质量必须位于 1–100。");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var bgra = new byte[checked((int)(image.Size.PixelCount * 4))];
        var rgba = image.Rgba.Span;
        for (var i = 0; i < rgba.Length; i += 4)
        {
            bgra[i] = rgba[i + 2];
            bgra[i + 1] = rgba[i + 1];
            bgra[i + 2] = rgba[i];
            bgra[i + 3] = rgba[i + 3];
        }

        var handle = GCHandle.Alloc(bgra, GCHandleType.Pinned);
        try
        {
            using var bitmap = new Bitmap(
                PixelFormat.Bgra8888,
                AlphaFormat.Unpremul,
                handle.AddrOfPinnedObject(),
                new PixelSize(image.Size.Width, image.Size.Height),
                new Vector(96d, 96d),
                checked(image.Size.Width * 4));
            using var output = new MemoryStream();
            BitmapEncoderOptions options = format switch
            {
                ImageOutputFormat.Png => PngBitmapEncoderOptions.Default,
                ImageOutputFormat.Jpeg => new JpegBitmapEncoderOptions { Quality = jpegQuality },
                _ => throw new ArgumentOutOfRangeException(nameof(format), format, "不支持的图片输出格式。")
            };
            bitmap.Save(output, options);
            return Task.FromResult(output.ToArray());
        }
        finally
        {
            handle.Free();
        }
    }
}
