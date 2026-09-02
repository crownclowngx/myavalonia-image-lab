namespace ImageLabPlugin.Domain.Watermarking;

internal enum PayloadContentType : byte
{
    Binary = 0,
    Text = 1,
    Json = 2
}

internal enum EmbeddingProfileId : byte
{
    Stealth = 1,
    Balanced = 2,
    Robust = 3
}

internal sealed record EmbeddingProfile(
    EmbeddingProfileId Id,
    string DisplayName,
    double DataQimStep,
    int DataRedundancy)
{
    public static EmbeddingProfile Resolve(EmbeddingProfileId id) => id switch
    {
        EmbeddingProfileId.Stealth => new(id, "隐蔽", 20d, 1),
        EmbeddingProfileId.Balanced => new(id, "均衡", 28d, 2),
        EmbeddingProfileId.Robust => new(id, "鲁棒", 36d, 3),
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "不支持的水印质量配置。")
    };
}

internal sealed class WatermarkPayload : IDisposable
{
    public const int MaximumPayloadBytes = 16 * 1024 * 1024;

    public WatermarkPayload(ReadOnlyMemory<byte> bytes, PayloadContentType contentType)
    {
        if (bytes.Length > MaximumPayloadBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes), "Payload 超过 V1 的 16 MiB 绝对安全上限。");
        }

        _bytes = bytes.ToArray();
        ContentType = contentType;
    }

    private byte[] _bytes;

    public ReadOnlyMemory<byte> Bytes => _bytes;
    public PayloadContentType ContentType { get; }

    /// <summary>尽早清除 Payload 的私有副本；调用方不得在释放后继续使用该对象。</summary>
    public void Dispose()
    {
        if (_bytes.Length == 0)
        {
            return;
        }

        System.Security.Cryptography.CryptographicOperations.ZeroMemory(_bytes);
        _bytes = [];
    }
}

internal readonly record struct CapacityEstimate(
    int CarrierSlots,
    int ControlSlots,
    int DataSlots,
    int MaximumProtectedBytes,
    int MaximumPayloadBytes,
    int RequiredPayloadBytes)
{
    public bool Fits => RequiredPayloadBytes <= MaximumPayloadBytes;
}

internal enum WatermarkDetectionStatus
{
    NoSupportedWatermark,
    DetectedKeyRequired,
    DetectedReady,
    RecoveredWithCorrections,
    RecoveredIntegrityValid,
    UnsupportedVersionOrProfile,
    UnrecoverableDamage,
    AuthenticationFailed,
    MalformedOrResourceRejected
}

internal enum IntegrityStatus
{
    NotChecked,
    Valid,
    Invalid
}

internal enum AuthenticityStatus
{
    NotSigned,
    Valid,
    Invalid,
    UnknownSigner
}

internal sealed record ExtractionReport(
    WatermarkDetectionStatus Status,
    string Summary,
    PayloadContentType? ContentType = null,
    ReadOnlyMemory<byte> Payload = default,
    EmbeddingProfileId? Profile = null,
    IntegrityStatus Integrity = IntegrityStatus.NotChecked,
    AuthenticityStatus Authenticity = AuthenticityStatus.NotSigned,
    int CorrectedSymbols = 0,
    double Confidence = 0d);

/// <summary>水印写入结果对外保留的两项稳定质量事实。</summary>
internal readonly record struct WatermarkQualityMetrics(double Psnr, double Ssim);

internal sealed record EmbedResult(
    byte[] EncodedImage,
    byte[] DifferencePreviewPng,
    byte[] SpectrumPreviewPng,
    string OutputFormat,
    CapacityEstimate Capacity,
    WatermarkQualityMetrics Quality,
    ExtractionReport SelfCheck);
