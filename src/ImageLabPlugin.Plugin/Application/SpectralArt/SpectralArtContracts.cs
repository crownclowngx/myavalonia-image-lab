using ImageLabPlugin.Domain.Shared.Analysis;
using ImageLabPlugin.Domain.Shared.Spectral;
using ImageLabPlugin.Domain.FrequencyFiltering;
using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Domain.SpectralArt;

namespace ImageLabPlugin.Application.SpectralArt;

internal static class SpectralArtProtocol
{
    public const string RecipeProtocol = "spectral-art-fft-amplitude-v1";
    public const string ReportProtocol = "spectral-art-report-v1";
    public const int RecipeSchema = 1;
    public const int SnapshotSchema = 1;
    public const double DefaultStrength = 2d;
    public const int DebounceMilliseconds = 200;
}

internal sealed record SpectralTextRasterRequest(
    string Text,
    string FontFamily,
    double FontSize,
    int FontWeight,
    int Padding,
    int MaximumEdge);

/// <summary>文字是平台字体边界；端口只返回已复制的 RGBA 像素，不把 Avalonia 类型传给应用或领域层。</summary>
internal interface ISpectralTextRasterizer
{
    Task<PixelImage> RasterizeAsync(SpectralTextRasterRequest request, CancellationToken cancellationToken);
}

internal sealed record SpectralPatternRequest(
    SpectralPatternSourceKind SourceKind,
    string Text,
    string ImagePath,
    string FontFamily,
    double FontSize,
    int FontWeight,
    int Padding,
    SpectralPatternNormalizationOptions Normalization);

internal sealed record SpectralCarrierRequest(string SourcePath);

/// <summary>工作区快照只保存非敏感轻量意图；绝对路径、原文字、Pattern 和频谱均不属于该模型。</summary>
internal sealed record SpectralArtSnapshotState(string? SourceDisplayName, string? PatternDisplayName,
    string SourceKind, string Sampling, string Fit, string Background, string FontFamily, double FontSize,
    int FontWeight, int Padding, int PatternWidth, int PatternHeight, double BinaryThreshold, bool Invert,
    double Strength, double Left, double Top, double Right, double Bottom, int Schema);

/// <summary>每个 Document Scope 独占的一次解码、Y 平面和只读全局 FFT。</summary>
internal sealed class SpectralArtSession : IDisposable
{
    private bool _disposed;

    public SpectralArtSession(
        string sourcePath,
        PixelImage sourceImage,
        ImageChannelPlane lumaPlane,
        FrequencySpectrum spectrum,
        PixelImage sourceSpectrumPreview,
        string sourceFingerprint)
    {
        SourcePath = sourcePath;
        SourceImage = sourceImage;
        LumaPlane = lumaPlane;
        Spectrum = spectrum;
        SourceSpectrumPreview = sourceSpectrumPreview;
        SourceFingerprint = sourceFingerprint;
        SessionFingerprint = Guid.NewGuid().ToString("N");
    }

    public string SourcePath { get; }
    public PixelImage SourceImage { get; }
    public ImageChannelPlane LumaPlane { get; }
    public FrequencySpectrum Spectrum { get; }
    public PixelImage SourceSpectrumPreview { get; }
    public string SourceFingerprint { get; }
    public string SessionFingerprint { get; }
    public bool IsDisposed => _disposed;
    public void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SpectralArtSession));
    }
    public void Dispose() => _disposed = true;
}

/// <summary>一次可复现渲染的完整数学参数，不包含载体路径或原始文字。</summary>
internal sealed record SpectralArtRecipe(
    SpectralPattern Pattern,
    SpectralArtRegion Region,
    SpectralPatternFitMode FitMode,
    double Strength)
{
    public string Fingerprint()
    {
        if (!double.IsFinite(Strength) || Strength is < 0d or > SpectralAmplitudeWriter.MaximumStrength)
            throw new ArgumentOutOfRangeException(nameof(Strength));
        var canonical = string.Join('|', SpectralArtProtocol.RecipeProtocol, Pattern.Fingerprint,
            Region.Left.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            Region.Top.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            Region.Right.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            Region.Bottom.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            (int)FitMode, Strength.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(canonical)))[..16].ToLowerInvariant();
    }
}

internal sealed record SpectralArtTimings(
    TimeSpan Mapping,
    TimeSpan WritingAndFrequencyDiagnostics,
    TimeSpan InverseAndProjection,
    TimeSpan SpatialDiagnostics);

internal sealed record SpectralArtResult(
    string SessionFingerprint,
    string SourceFingerprint,
    string PatternFingerprint,
    string RecipeFingerprint,
    string MappingFingerprint,
    PixelImage Output,
    PixelImage PatternPreview,
    PixelImage MappingPreview,
    PixelImage SourceSpectrumPreview,
    PixelImage ResultSpectrumPreview,
    PixelImage SpectrumDifferencePreview,
    ChannelDifferenceProjection Difference2X,
    ChannelDifferenceProjection Difference4X,
    ChannelDifferenceProjection Difference8X,
    FullReferenceQualityMetrics Quality,
    SpectralRawStatistics Raw,
    SpectralFrequencyDiagnostics Frequency,
    SpectralArtTimings Timings);

internal sealed record SpectralArtReport(
    string Protocol,
    int Schema,
    string SourceFingerprint,
    int Width,
    int Height,
    int PaddedWidth,
    int PaddedHeight,
    SpectralPatternSourceKind PatternSource,
    int PatternWidth,
    int PatternHeight,
    string PatternFingerprint,
    SpectralArtRegion Region,
    double Strength,
    SpectralFrequencyDiagnostics Frequency,
    SpectralRawStatistics Raw,
    FullReferenceQualityMetrics Quality,
    SpectralArtTimings Timings,
    string Limitation);

internal interface IPrepareSpectralArtCarrierUseCase
{
    Task<SpectralArtSession> ExecuteAsync(SpectralCarrierRequest request, CancellationToken cancellationToken);
}

internal interface ICreateSpectralPatternUseCase
{
    Task<SpectralPattern> ExecuteAsync(SpectralPatternRequest request, CancellationToken cancellationToken);
}

internal interface IRenderSpectralArtUseCase
{
    Task<SpectralArtResult> ExecuteAsync(SpectralArtSession session, SpectralArtRecipe recipe,
        CancellationToken cancellationToken);
}

internal interface ISpectralArtRecipeSerializer
{
    byte[] Serialize(SpectralArtRecipe recipe);
    SpectralArtRecipe Deserialize(ReadOnlySpan<byte> json);
}

internal interface ISpectralArtReportSerializer
{
    byte[] SerializeJson(SpectralArtReport report);
    byte[] SerializeCsv(SpectralArtReport report);
}

internal interface ISpectralArtSnapshotSerializer
{
    System.Text.Json.JsonElement Serialize(SpectralArtSnapshotState state);
    SpectralArtSnapshotState? Deserialize(System.Text.Json.JsonElement payload);
}

internal interface IExportSpectralArtImageUseCase
{
    Task ExecuteAsync(SpectralArtSession session, SpectralArtResult result,
        SpectralArtRecipe expectedRecipe, string outputPath, CancellationToken cancellationToken);
}

internal interface IImportSpectralArtRecipeUseCase
{
    Task<SpectralArtRecipe> ExecuteAsync(string path, CancellationToken cancellationToken);
}

internal interface IExportSpectralArtRecipeUseCase
{
    Task ExecuteAsync(SpectralArtRecipe recipe, string path, CancellationToken cancellationToken);
}

internal interface IExportSpectralArtReportUseCase
{
    Task ExecuteAsync(SpectralArtReport report, string path, bool csv, CancellationToken cancellationToken);
}
