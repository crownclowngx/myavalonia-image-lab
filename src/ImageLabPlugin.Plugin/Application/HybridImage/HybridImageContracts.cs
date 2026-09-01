using ImageLabPlugin.Domain.Frequency;
using ImageLabPlugin.Domain.FrequencyFiltering;
using ImageLabPlugin.Domain.HybridImage;
using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Application.HybridImage;

/// <summary>集中冻结 Hybrid Image 的协议名、schema、代理和有界读取常量。</summary>
internal static class HybridImageProtocol
{
    public const string Recipe = "hybrid-image-v1";
    public const string Report = "hybrid-image-report-v1";
    public const int Schema = 1;
    public const int SnapshotSchema = 1;
    public const int ProxyMaximumEdge = 1024;
    public const int MaximumJsonBytes = 256 * 1024;
}

internal sealed record PrepareHybridInputsRequest(string PathA, string PathB, int ProxyMaximumEdge = HybridImageProtocol.ProxyMaximumEdge);
internal sealed record SolveHybridAlignmentRequest(IReadOnlyList<HybridAlignmentPointPair> Points);

internal enum HybridSpectrumKind { SourceA, LowA, SourceB, HighB, Raw }

/// <summary>保存有界 double 频谱源，并在用户真正查看某项时才构建 FFT 与显示 Bitmap 的像素事实。</summary>
/// <remarks>
/// 构造阶段只为确定五项共同量程短暂建立频谱，随后立即释放 Complex 数组；实例长期保存的是最大边 1024
/// 的亮度代理。`Project` 每次只建立一项频谱，不会缓存五份完整 Complex[] 或 PixelImage。
/// </remarks>
internal sealed class HybridSpectrumBundle
{
    private readonly IReadOnlyDictionary<HybridSpectrumKind, HybridLumaPlane> _planes;
    private readonly FrequencySpectrumBuilder _builder;
    private readonly SpectrumProjector _projector;

    public HybridSpectrumBundle(SpectrumDisplayScale sharedScale,
        IReadOnlyDictionary<HybridSpectrumKind, HybridLumaPlane> planes,
        FrequencySpectrumBuilder builder, SpectrumProjector projector,
        int paddedWidth, int paddedHeight)
    {
        SharedScale = sharedScale; _planes = planes; _builder = builder; _projector = projector;
        PaddedWidth = paddedWidth; PaddedHeight = paddedHeight;
    }

    public SpectrumDisplayScale SharedScale { get; }
    public int PaddedWidth { get; }
    public int PaddedHeight { get; }

    public PixelImage Project(HybridSpectrumKind kind, CancellationToken cancellationToken = default)
    {
        if (!_planes.TryGetValue(kind, out var plane)) throw new ArgumentOutOfRangeException(nameof(kind));
        var values = new double[plane.Values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            if ((i & 16383) == 0) cancellationToken.ThrowIfCancellationRequested();
            values[i] = plane.Values.Span[i] * 255d;
        }
        var spectrum = _builder.Build(new ImageChannelPlane(plane.Size, ImageChannel.Luma, values), cancellationToken);
        return _projector.CreateMagnitude(spectrum, SharedScale, cancellationToken);
    }
}

internal sealed record HybridCutoffDiagnostics(
    double LowCyclesPerImage,
    double HighCyclesPerImage,
    double LowDisplayRadiusPixels,
    double HighDisplayRadiusPixels,
    string Explanation);

internal sealed record HybridRenderResult(
    string SessionFingerprint,
    string RecipeFingerprint,
    long Generation,
    bool IsFullSize,
    HybridAlignmentSolution Alignment,
    HybridCropRectangle Crop,
    double CoverageRatio,
    HybridCompositionResult Composition,
    IReadOnlyList<HybridScalePreview> Scales,
    PixelImage EdgeOverlay,
    HybridAlignmentDiagnostics Diagnostics,
    HybridSpectrumBundle Spectra,
    HybridCutoffDiagnostics Cutoff,
    TimeSpan Elapsed);

internal sealed record HybridImageSnapshotState(
    string? DisplayNameA,
    string? DisplayNameB,
    IReadOnlyList<HybridAlignmentPointPair> Points,
    HybridNormalizedCrop Crop,
    double LowSigmaPixels,
    double HighSigmaPixels,
    double LowGain,
    double HighGain,
    string SelectedTab,
    int Schema);

internal sealed record HybridImageReport(
    string Protocol,
    int Schema,
    string FingerprintA,
    string FingerprintB,
    ImageSize SizeA,
    ImageSize SizeB,
    string RecipeFingerprint,
    HybridAlignmentDiagnostics Alignment,
    HybridCropRectangle Crop,
    HybridRawStatistics Raw,
    IReadOnlyList<ImageSize> ScaleSizes,
    double LowSigmaPixels,
    double HighSigmaPixels,
    double LowFiftyPercentCutoff,
    double HighFiftyPercentCutoff,
    double LowGain,
    double HighGain,
    long ElapsedMilliseconds,
    string ImplementationVersion,
    string Limitation);

internal interface IPrepareHybridInputsUseCase
{
    Task<HybridImageSession> ExecuteAsync(PrepareHybridInputsRequest request, CancellationToken cancellationToken);
}

internal interface ISolveHybridAlignmentUseCase
{
    Task<HybridAlignmentState> ExecuteAsync(HybridImageSession session, SolveHybridAlignmentRequest request,
        CancellationToken cancellationToken);
}

internal interface IRenderHybridPreviewUseCase
{
    Task<HybridRenderResult> ExecuteAsync(HybridImageSession session, HybridImageRecipe recipe,
        long generation, CancellationToken cancellationToken);
}

internal interface IRenderHybridFullSizeUseCase
{
    Task<HybridRenderResult> ExecuteAsync(HybridImageSession session, HybridImageRecipe recipe,
        long generation, CancellationToken cancellationToken);
}

internal interface IHybridImageRecipeSerializer
{
    byte[] Serialize(HybridImageRecipe recipe, string fingerprintA, string fingerprintB);
    HybridImageRecipe Deserialize(ReadOnlySpan<byte> json, out string fingerprintA, out string fingerprintB);
}

internal interface IHybridImageReportSerializer
{
    byte[] SerializeJson(HybridImageReport report);
    byte[] SerializeCsv(HybridImageReport report);
}

internal interface IHybridImageSnapshotSerializer
{
    System.Text.Json.JsonElement Serialize(HybridImageSnapshotState state);
    HybridImageSnapshotState? Deserialize(System.Text.Json.JsonElement payload);
}

internal interface IExportHybridImageUseCase
{
    Task ExecuteAsync(HybridImageSession session, HybridRenderResult result, HybridImageRecipe recipe,
        string outputPath, CancellationToken cancellationToken);
}

internal interface IImportHybridRecipeUseCase
{
    Task<(HybridImageRecipe Recipe, string FingerprintA, string FingerprintB)> ExecuteAsync(
        string path, CancellationToken cancellationToken);
}

internal interface IExportHybridRecipeUseCase
{
    Task ExecuteAsync(HybridImageRecipe recipe, HybridImageSession session, string path,
        CancellationToken cancellationToken);
}

internal interface IExportHybridReportUseCase
{
    Task ExecuteAsync(HybridImageReport report, string path, bool csv, CancellationToken cancellationToken);
}
