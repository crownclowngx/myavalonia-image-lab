using System.Security.Cryptography;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.Frequency;
using ImageLabPlugin.Domain.Imaging;
using ImageLabPlugin.Domain.Watermarking;
using ImageLabPlugin.Infrastructure.Watermarking;

namespace ImageLabPlugin.Application.Watermarking;

internal sealed record EmbedWatermarkRequest(
    string SourcePath,
    WatermarkPayload Payload,
    EmbeddingProfileId Profile,
    string? Password,
    ImageOutputFormat OutputFormat,
    int JpegQuality = 95);

/// <summary>容量估算输入端口，供 Document 依赖抽象并在测试中替换慢速实现。</summary>
internal interface IEstimateWatermarkCapacityUseCase
{
    Task<CapacityEstimate> ExecuteAsync(
        string sourcePath,
        EmbeddingProfileId profile,
        int payloadLength,
        bool encrypted,
        CancellationToken cancellationToken);
}

internal interface IInspectWatermarkUseCase
{
    Task<(PixelImage Image, HeaderReadResult? Header, ExtractionReport Report)> ExecuteAsync(
        string sourcePath,
        CancellationToken cancellationToken);
}

internal interface IExtractWatermarkUseCase
{
    Task<(PixelImage Image, ExtractionReport Report)> ExecuteAsync(
        string sourcePath,
        string? password,
        CancellationToken cancellationToken);

    Task<ExtractionReport> ExecuteAsync(
        ReadOnlyMemory<byte> encodedImage,
        string? password,
        CancellationToken cancellationToken);

    ExtractionReport Extract(PixelImage image, string? password, CancellationToken cancellationToken);
}

internal interface IEmbedWatermarkUseCase
{
    Task<EmbedResult> ExecuteAsync(EmbedWatermarkRequest request, CancellationToken cancellationToken);
}

internal sealed class EstimateWatermarkCapacityUseCase(IImageCodec codec, FrequencyWatermarkCarrier carrier)
    : IEstimateWatermarkCapacityUseCase
{
    public async Task<CapacityEstimate> ExecuteAsync(
        string sourcePath,
        EmbeddingProfileId profile,
        int payloadLength,
        bool encrypted,
        CancellationToken cancellationToken)
    {
        var image = await codec.DecodeAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        return carrier.Estimate(image, profile, payloadLength, encrypted);
    }
}

internal sealed class InspectWatermarkUseCase(IImageCodec codec, FrequencyWatermarkCarrier carrier)
    : IInspectWatermarkUseCase
{
    public async Task<(PixelImage Image, HeaderReadResult? Header, ExtractionReport Report)> ExecuteAsync(
        string sourcePath,
        CancellationToken cancellationToken)
    {
        var image = await codec.DecodeAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        try
        {
            var header = carrier.ReadHeader(image, cancellationToken);
            var needsKey = header.Header.Flags.HasFlag(FrameFlags.Encrypted);
            return (
                image,
                header,
                new ExtractionReport(
                    needsKey ? WatermarkDetectionStatus.DetectedKeyRequired : WatermarkDetectionStatus.DetectedReady,
                    needsKey ? "检测到 V1 水印，需要密码才能提取。" : "检测到可提取的 V1 水印。",
                    header.Header.ContentType,
                    Profile: header.Header.Profile,
                    CorrectedSymbols: header.CorrectedSymbols,
                    Confidence: header.Confidence));
        }
        catch (NotSupportedException exception)
        {
            return (image, null, new ExtractionReport(
                WatermarkDetectionStatus.UnsupportedVersionOrProfile,
                exception.Message));
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException)
        {
            return (image, null, new ExtractionReport(
                WatermarkDetectionStatus.NoSupportedWatermark,
                "未检测到受支持的 ImageLab V1 水印。"));
        }
    }
}

internal sealed class ExtractWatermarkUseCase(
    IImageCodec codec,
    FrequencyWatermarkCarrier carrier,
    WatermarkFrameProtocol frameProtocol) : IExtractWatermarkUseCase
{
    public async Task<(PixelImage Image, ExtractionReport Report)> ExecuteAsync(
        string sourcePath,
        string? password,
        CancellationToken cancellationToken)
    {
        var image = await codec.DecodeAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        return (image, Extract(image, password, cancellationToken));
    }

    public async Task<ExtractionReport> ExecuteAsync(
        ReadOnlyMemory<byte> encodedImage,
        string? password,
        CancellationToken cancellationToken)
    {
        var image = await codec.DecodeAsync(encodedImage, cancellationToken).ConfigureAwait(false);
        return Extract(image, password, cancellationToken);
    }

    public ExtractionReport Extract(PixelImage image, string? password, CancellationToken cancellationToken)
    {
        HeaderReadResult headerRead;
        try
        {
            headerRead = carrier.ReadHeader(image, cancellationToken);
        }
        catch (NotSupportedException exception)
        {
            return new ExtractionReport(WatermarkDetectionStatus.UnsupportedVersionOrProfile, exception.Message);
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException)
        {
            return new ExtractionReport(
                WatermarkDetectionStatus.NoSupportedWatermark,
                "未检测到受支持的 ImageLab V1 水印。");
        }

        if (headerRead.Header.Flags.HasFlag(FrameFlags.Encrypted) && string.IsNullOrEmpty(password))
        {
            return new ExtractionReport(
                WatermarkDetectionStatus.DetectedKeyRequired,
                "检测到 V1 水印，需要密码才能提取。",
                headerRead.Header.ContentType,
                Profile: headerRead.Header.Profile,
                CorrectedSymbols: headerRead.CorrectedSymbols,
                Confidence: headerRead.Confidence);
        }

        try
        {
            var mappingKey = frameProtocol.ResolveMappingKey(headerRead.Header, password);
            try
            {
                var dataRead = carrier.ReadData(image, headerRead.Header, mappingKey, cancellationToken);
                var decoded = frameProtocol.DecodeData(headerRead.Header, dataRead.EncodedData, password);
                using (decoded.Payload)
                {
                    var totalCorrections = headerRead.CorrectedSymbols + decoded.CorrectedSymbols;
                    return new ExtractionReport(
                        totalCorrections > 0
                            ? WatermarkDetectionStatus.RecoveredWithCorrections
                            : WatermarkDetectionStatus.RecoveredIntegrityValid,
                        totalCorrections > 0
                            ? $"Payload 已恢复，Reed-Solomon 共修复 {totalCorrections} 个符号。"
                            : "Payload 已完整恢复并通过完整性验证。",
                        decoded.Payload.ContentType,
                        decoded.Payload.Bytes.ToArray(),
                        headerRead.Header.Profile,
                        decoded.Integrity,
                        AuthenticityStatus.NotSigned,
                        totalCorrections,
                        Math.Min(headerRead.Confidence, dataRead.Confidence));
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(mappingKey);
            }
        }
        catch (CryptographicException)
        {
            return AuthenticationFailure(headerRead);
        }
        catch (Exception exception) when (
            exception is InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            return headerRead.Header.Flags.HasFlag(FrameFlags.Encrypted)
                ? AuthenticationFailure(headerRead)
                : new ExtractionReport(
                    WatermarkDetectionStatus.UnrecoverableDamage,
                    "检测到 V1 水印，但数据损坏超过当前纠错能力。",
                    headerRead.Header.ContentType,
                    Profile: headerRead.Header.Profile,
                    Integrity: IntegrityStatus.Invalid,
                    CorrectedSymbols: headerRead.CorrectedSymbols,
                    Confidence: headerRead.Confidence);
        }
    }

    private static ExtractionReport AuthenticationFailure(HeaderReadResult headerRead) => new(
        WatermarkDetectionStatus.AuthenticationFailed,
        "密码错误或图片数据已经改变，AES-GCM 无法完成认证。",
        headerRead.Header.ContentType,
        Profile: headerRead.Header.Profile,
        Integrity: IntegrityStatus.Invalid,
        CorrectedSymbols: headerRead.CorrectedSymbols,
        Confidence: headerRead.Confidence);
}

internal sealed class EmbedWatermarkUseCase(
    IImageCodec codec,
    WatermarkFrameProtocol frameProtocol,
    FrequencyWatermarkCarrier carrier,
    FrequencySpectrumProjector spectrumProjector,
    IExtractWatermarkUseCase extractor) : IEmbedWatermarkUseCase
{
    public async Task<EmbedResult> ExecuteAsync(
        EmbedWatermarkRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var source = await codec.DecodeAsync(request.SourcePath, cancellationToken).ConfigureAwait(false);
        var capacity = carrier.Estimate(
            source,
            request.Profile,
            request.Payload.Bytes.Length,
            !string.IsNullOrEmpty(request.Password));
        if (!capacity.Fits)
        {
            throw new InvalidOperationException(
                $"Payload 需要约 {capacity.RequiredPayloadBytes:N0} 字节，当前配置最多承载 {capacity.MaximumPayloadBytes:N0} 字节。");
        }

        var frame = frameProtocol.Encode(request.Payload, request.Profile, request.Password);
        try
        {
            var outputImage = carrier.Embed(source, frame, cancellationToken);
            var quality = ImageQualityCalculator.Compare(source, outputImage);
            // 差异和频谱只供解释，不参与协议。先限制到 1024 像素边长，避免大图再复制多组全尺寸缓冲区。
            var analysisSource = ImagePreviewProjector.FitWithin(source);
            var analysisOutput = ImagePreviewProjector.FitWithin(outputImage);
            var differenceImage = ImageDifferenceProjector.Create(analysisSource, analysisOutput);
            var spectrumImage = spectrumProjector.Create(analysisOutput, cancellationToken);
            var encodedImage = await codec.EncodeAsync(
                outputImage,
                request.OutputFormat,
                request.JpegQuality,
                cancellationToken).ConfigureAwait(false);
            var differencePreview = await codec.EncodeAsync(
                differenceImage,
                ImageOutputFormat.Png,
                jpegQuality: 95,
                cancellationToken).ConfigureAwait(false);
            var spectrumPreview = await codec.EncodeAsync(
                spectrumImage,
                ImageOutputFormat.Png,
                jpegQuality: 95,
                cancellationToken).ConfigureAwait(false);
            var selfCheck = await extractor.ExecuteAsync(encodedImage, request.Password, cancellationToken)
                .ConfigureAwait(false);
            if (selfCheck.Status is not (
                WatermarkDetectionStatus.RecoveredIntegrityValid or
                WatermarkDetectionStatus.RecoveredWithCorrections) ||
                !selfCheck.Payload.Span.SequenceEqual(request.Payload.Bytes.Span))
            {
                throw new InvalidOperationException($"输出图片回读自检失败：{selfCheck.Summary}");
            }

            return new EmbedResult(
                encodedImage,
                differencePreview,
                spectrumPreview,
                request.OutputFormat == ImageOutputFormat.Png ? "PNG" : "JPEG",
                capacity,
                quality,
                selfCheck);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(frame.MappingKey);
        }
    }
}
