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
internal interface IImageFileDialog
{
    Task<string?> PickImageAsync(CancellationToken cancellationToken);
    Task<string?> PickOutputImageAsync(string suggestedName, CancellationToken cancellationToken);
}

/// <summary>仅暴露水印 Payload 文件选择意图，避免图片分析用例依赖无关能力。</summary>
internal interface IPayloadFileDialog
{
    Task<string?> PickPayloadAsync(CancellationToken cancellationToken);
    Task<string?> PickPayloadExportAsync(string suggestedName, CancellationToken cancellationToken);
}

/// <summary>只表达“选择比较摘要输出位置”的意图，避免比较功能依赖图片或 Payload 保存选项。</summary>
internal interface IComparisonReportFileDialog
{
    Task<string?> PickSummaryOutputAsync(string suggestedName, CancellationToken cancellationToken);
}

/// <summary>鲁棒性报告的窄文件意图；JSON 与 CSV 分开选择，避免一个万能文件服务持续膨胀。</summary>
internal interface IRobustnessReportFileDialog
{
    Task<string?> PickJsonOutputAsync(string suggestedName, CancellationToken cancellationToken);
    Task<string?> PickCsvOutputAsync(string suggestedName, CancellationToken cancellationToken);
}

/// <summary>只表达“选择感知指纹 JSON 报告位置”的文件意图。</summary>
internal interface IFingerprintReportFileDialog
{
    Task<string?> PickFingerprintJsonOutputAsync(string suggestedName, CancellationToken cancellationToken);
}

/// <summary>LSB 报告只允许选择 JSON/CSV 目的地，不暴露图片或 Payload 保存能力。</summary>
internal interface ILsbReportFileDialog
{
    Task<string?> PickLsbJsonOutputAsync(string suggestedName, CancellationToken cancellationToken);
    Task<string?> PickLsbCsvOutputAsync(string suggestedName, CancellationToken cancellationToken);
}

/// <summary>小波实验只暴露 JSON/CSV 报告保存意图，避免继续扩大图片文件端口。</summary>
internal interface IWaveletReportFileDialog
{
    Task<string?> PickWaveletJsonOutputAsync(string suggestedName, CancellationToken cancellationToken);
    Task<string?> PickWaveletCsvOutputAsync(string suggestedName, CancellationToken cancellationToken);
}

/// <summary>频谱遮罩配方只暴露 JSON 导入与导出意图，不扩大图片文件对话框。</summary>
internal interface IFrequencyMaskRecipeFileDialog
{
    Task<string?> PickRecipeInputAsync(CancellationToken cancellationToken);
    Task<string?> PickRecipeOutputAsync(string suggestedName, CancellationToken cancellationToken);
}

/// <summary>LSB 二进制载荷的有界读取端口；实现必须在读取前后都执行 64 KiB 门禁。</summary>
internal interface ILsbPayloadFileReader
{
    Task<byte[]> ReadAsync(string path, CancellationToken cancellationToken);
}

/// <summary>文本剪贴板窄端口；失败以 false 返回，Document 可以保留有效比较结果并提示重试。</summary>
internal interface ITextClipboard
{
    Task<bool> TrySetTextAsync(string text, CancellationToken cancellationToken);
}

internal interface IRandomSource
{
    void Fill(Span<byte> destination);
}

internal interface IAtomicFileWriter
{
    Task WriteAsync(string targetPath, ReadOnlyMemory<byte> content, CancellationToken cancellationToken);
}

/// <summary>有界文本读取端口；配方用例不直接依赖文件系统静态 API。</summary>
internal interface ITextFileReader
{
    Task<byte[]> ReadAsync(string path, int maximumBytes, CancellationToken cancellationToken);
}
