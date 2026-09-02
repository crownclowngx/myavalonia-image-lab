using ImageLabPlugin.Domain.Shared.Analysis;
using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Domain.Wavelets;

namespace ImageLabPlugin.Application.Wavelets;

/// <summary>一次解码的源图、可选参考图与分析代理；每个 Document 独占一个实例。</summary>
internal sealed class WaveletSession : IDisposable
{
    private bool _disposed;
    public WaveletSession(string sourcePath, PixelImage sourceImage, PixelImage analysisProxy,
        int analysisMaximumEdge, string? referencePath = null, PixelImage? referenceImage = null,
        PixelImage? referenceProxy = null)
    {
        SourcePath = sourcePath; SourceImage = sourceImage; AnalysisProxy = analysisProxy;
        AnalysisMaximumEdge = analysisMaximumEdge; ReferencePath = referencePath; ReferenceImage = referenceImage;
        ReferenceProxy = referenceProxy;
    }
    public string SourcePath { get; }
    public PixelImage SourceImage { get; }
    public PixelImage AnalysisProxy { get; }
    public int AnalysisMaximumEdge { get; }
    public string? ReferencePath { get; }
    public PixelImage? ReferenceImage { get; }
    public PixelImage? ReferenceProxy { get; }
    public bool IsDisposed => _disposed;
    public void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(WaveletSession)); }
    public void Dispose() => _disposed = true;
}

internal sealed record WaveletAnalysisResult(
    ImageChannelPlane OriginalPlane,
    WaveletPyramid Pyramid,
    WaveletNoiseEstimate Noise,
    WaveletProjection Projection,
    string RecipeFingerprint,
    bool IsFullSize,
    TimeSpan Elapsed);

internal sealed record WaveletDenoiseResult(
    WaveletPyramid Pyramid,
    WaveletReconstructionResult Reconstruction,
    WaveletThresholdStatistics ThresholdStatistics,
    FullReferenceQualityMetrics? ReferenceQuality,
    string RecipeFingerprint,
    bool IsFullSize,
    TimeSpan Elapsed);

internal sealed record WaveletLevelReconstructionResult(int TargetLevel, ImageChannelPlane Plane, PixelImage Preview);

internal sealed record WaveletScanCase(
    int Sequence,
    int Levels,
    double Threshold,
    WaveletThresholdStatistics Statistics,
    double ResidualRms,
    double? PsnrLuma,
    double? SsimLuma,
    TimeSpan Elapsed);

internal sealed record WaveletScanResult(IReadOnlyList<WaveletScanCase> Cases, bool Canceled, string MetricBoundary);

internal sealed record WatermarkBenchmarkCapacity(string CarrierId, int MaximumPayloadBytes);
internal interface IWatermarkBenchmarkReadContext;
internal sealed record WatermarkBenchmarkEmbedding(PixelImage Image, bool IntegrityValid, byte[] RecoveredPayload,
    double Confidence, double? RawBitErrorRate, IWatermarkBenchmarkReadContext ReadContext);
internal sealed record WatermarkBenchmarkRead(bool IntegrityValid, double Confidence, double? RawBitErrorRate);

/// <summary>公平比较用窄载体端口；DCT 与 DWT 各自把私有协议适配成相同结果。</summary>
internal interface IWatermarkBenchmarkCarrier
{
    string CarrierId { get; }
    WatermarkBenchmarkCapacity Estimate(PixelImage source, int payloadLength);
    Task<WatermarkBenchmarkEmbedding> EmbedAndReadAsync(PixelImage source, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken);
    Task<WatermarkBenchmarkRead> ReadAsync(PixelImage image, WatermarkBenchmarkEmbedding baseline,
        ReadOnlyMemory<byte> expectedPayload, CancellationToken cancellationToken);
}

internal sealed record WatermarkBenchmarkCase(
    string CaseId,
    string CarrierId,
    bool IntegrityValid,
    double Confidence,
    double? RawBitErrorRate,
    FullReferenceQualityMetrics Imperceptibility);

internal sealed record WatermarkCarrierBenchmarkReport(
    string Schema,
    int PayloadLength,
    IReadOnlyList<WatermarkBenchmarkCapacity> Capacities,
    IReadOnlyList<WatermarkBenchmarkCase> Cases,
    IReadOnlyList<string> Limitations,
    DateTimeOffset CreatedAtUtc);

internal sealed record WaveletExperimentReport(
    string Schema,
    string SourcePath,
    string RecipeFingerprint,
    string Transform,
    string Channel,
    int Levels,
    double Threshold,
    IReadOnlyList<WaveletScanCase> ScanCases,
    WatermarkCarrierBenchmarkReport? WatermarkBenchmark,
    IReadOnlyList<string> Limitations,
    DateTimeOffset CreatedAtUtc);

internal interface IPrepareWaveletSessionUseCase
{
    Task<WaveletSession> ExecuteAsync(string sourcePath, string? referencePath, int analysisMaximumEdge, CancellationToken cancellationToken);
}
internal interface IDecomposeWaveletUseCase
{
    Task<WaveletAnalysisResult> ExecuteAsync(WaveletSession session, WaveletDenoiseRecipe recipe, bool fullSize,
        int projectionLevel, WaveletSubband projectionSubband, WaveletProjectionMode projectionMode, CancellationToken cancellationToken);
}
internal interface IDenoiseWaveletUseCase
{
    Task<WaveletDenoiseResult> ExecuteAsync(WaveletSession session, WaveletAnalysisResult analysis,
        WaveletDenoiseRecipe recipe, CancellationToken cancellationToken);
}
internal interface IReconstructWaveletLevelUseCase
{
    Task<WaveletLevelReconstructionResult> ExecuteAsync(WaveletAnalysisResult analysis, int targetLevel, CancellationToken cancellationToken);
}
internal interface IRunWaveletQualityScanUseCase
{
    Task<WaveletScanResult> ExecuteAsync(WaveletSession session, WaveletDenoiseRecipe template,
        IReadOnlyList<double> thresholds, IReadOnlyList<int> levels, CancellationToken cancellationToken);
}
internal interface IRunWatermarkCarrierBenchmarkUseCase
{
    Task<WatermarkCarrierBenchmarkReport> ExecuteAsync(PixelImage source, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken);
}
internal interface IExportWaveletImageUseCase
{
    Task ExecuteAsync(WaveletDenoiseResult result, string expectedFingerprint, string path, CancellationToken cancellationToken);
}
internal interface IExportWaveletReportUseCase
{
    Task ExecuteAsync(WaveletExperimentReport report, string path, bool csv, CancellationToken cancellationToken);
}

internal interface IWaveletReportSerializer
{
    byte[] SerializeJson(WaveletExperimentReport report);
    byte[] SerializeCsv(WaveletExperimentReport report);
}
