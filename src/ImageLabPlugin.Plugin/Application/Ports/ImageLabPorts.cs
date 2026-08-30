using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Application.Ports;

internal enum ImageOutputFormat
{
    Png,
    Jpeg
}

/// <summary>隔离具体图片编解码实现，使应用用例只处理已验证的 RGBA 像素。</summary>
internal interface IImageCodec
{
    Task<PixelImage> DecodeAsync(string path, CancellationToken cancellationToken);
    Task<PixelImage> DecodeAsync(ReadOnlyMemory<byte> encodedImage, CancellationToken cancellationToken);
    Task<byte[]> EncodeAsync(
        PixelImage image,
        ImageOutputFormat format,
        int jpegQuality,
        CancellationToken cancellationToken);
}

/// <summary>隔离 Host/Standalone 的文件选择交互，不让应用层依赖 Avalonia Storage 类型。</summary>
internal interface IImageLabFileDialog
{
    Task<string?> PickImageAsync(CancellationToken cancellationToken);
    Task<string?> PickPayloadAsync(CancellationToken cancellationToken);
    Task<string?> PickOutputImageAsync(string suggestedName, CancellationToken cancellationToken);
    Task<string?> PickPayloadExportAsync(string suggestedName, CancellationToken cancellationToken);
}

internal interface IRandomSource
{
    void Fill(Span<byte> destination);
}

internal interface IAtomicFileWriter
{
    Task WriteAsync(string targetPath, ReadOnlyMemory<byte> content, CancellationToken cancellationToken);
}
