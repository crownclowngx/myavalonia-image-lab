using System.Security.Cryptography;
using ImageLabPlugin.Domain.Imaging;
using ImageLabPlugin.Domain.Steganography;

namespace ImageLabPlugin.Application.LsbSteganography;

/// <summary>单个 Document 独占的大对象会话；算法 singleton 不缓存图片。</summary>
/// <remarks>
/// 会话最多长期持有 source、stego、当前 attacked 和当前 Frame/位置事实。换载体或关闭时主动清零 Frame，
/// 并切断完整图片引用；报告和快照都不能取得原始 Frame 或 Payload。
/// </remarks>
internal sealed class LsbExperimentSession : IDisposable
{
    private bool _disposed;
    private byte[] _frame = [];

    public LsbExperimentSession(string sourcePath, PixelImage sourceImage, LsbSlotLayout layout)
    {
        SourcePath = sourcePath;
        SourceImage = sourceImage;
        Layout = layout;
    }

    public string SourcePath { get; }
    public PixelImage SourceImage { get; private set; }
    public LsbSlotLayout Layout { get; }
    public PixelImage? StegoImage { get; private set; }
    public PixelImage? AttackedImage { get; private set; }
    public LsbRecipe? Recipe { get; private set; }
    public LsbEmbeddingFacts? EmbeddingFacts { get; private set; }
    public LsbStatisticsComparison? Statistics { get; private set; }
    public LsbExtractionResult? SelfCheck { get; private set; }
    public LsbFragilityResult? Fragility { get; private set; }
    public ReadOnlyMemory<byte> Frame { get { ThrowIfDisposed(); return _frame; } }
    public bool HasVerifiedStego => StegoImage is not null && SelfCheck?.Status == LsbReadStatus.Success;
    public bool IsDisposed => _disposed;

    public void CommitEmbedding(byte[] frame, LsbRecipe recipe, LsbEmbeddingResult embedding, LsbExtractionResult selfCheck, LsbStatisticsComparison statistics)
    {
        ThrowIfDisposed();
        CryptographicOperations.ZeroMemory(_frame);
        _frame = frame.ToArray();
        Recipe = recipe;
        StegoImage = embedding.Image;
        EmbeddingFacts = embedding.Facts;
        SelfCheck = selfCheck;
        Statistics = statistics;
        AttackedImage = null;
        Fragility = null;
    }

    public void CommitFragility(LsbFragilityResult result)
    {
        ThrowIfDisposed();
        AttackedImage = result.Image;
        Fragility = result;
    }

    public void InvalidateResults()
    {
        ThrowIfDisposed();
        CryptographicOperations.ZeroMemory(_frame);
        _frame = [];
        StegoImage = null; AttackedImage = null; Recipe = null; EmbeddingFacts = null; Statistics = null; SelfCheck = null; Fragility = null;
    }

    public void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LsbExperimentSession));
    }

    public void Dispose()
    {
        if (_disposed) return;
        InvalidateResults();
        _disposed = true;
        SourceImage = new PixelImage(new ImageSize(1, 1), [0, 0, 0, 0]);
    }
}

internal sealed record LsbPreparedSession(LsbExperimentSession Session, LsbCapacity EmptyPayloadCapacity);
internal sealed record LsbEmbedUseCaseResult(LsbEmbeddingFacts Facts, LsbExtractionResult SelfCheck, LsbStatisticsComparison Statistics, LsbPreviewProjection Preview);
internal sealed record LsbImageExportResult(string OutputPath, int EncodedBytes, LsbExtractionResult SelfCheck);
internal sealed record LsbReportExportResult(string OutputPath, string Format, int BytesWritten);

internal interface IPrepareLsbExperimentUseCase { Task<LsbPreparedSession> ExecuteAsync(string sourcePath, LsbRecipe recipe, CancellationToken cancellationToken); }
internal interface IEstimateLsbCapacityUseCase { LsbCapacity Execute(LsbExperimentSession session, LsbRecipe recipe, int payloadLength); }
internal interface IEmbedAndAnalyzeLsbUseCase { Task<LsbEmbedUseCaseResult> ExecuteAsync(LsbExperimentSession session, LsbPayload payload, LsbRecipe recipe, LsbStatisticsScope scope, CancellationToken cancellationToken); }
internal interface IExtractLsbPayloadUseCase { Task<LsbExtractionResult> ExecuteAsync(PixelImage image, LsbRecipe recipe, CancellationToken cancellationToken); }
internal interface IRunLsbFragilityUseCase { Task<LsbFragilityResult> ExecuteAsync(LsbExperimentSession session, LsbFragilityPreset preset, CancellationToken cancellationToken); }
internal interface IExportLsbImageUseCase { Task<LsbImageExportResult> ExecuteAsync(LsbExperimentSession session, string outputPath, CancellationToken cancellationToken); }
internal interface IExportLsbReportUseCase { Task<LsbReportExportResult> ExecuteAsync(LsbExperimentSession session, string outputPath, string format, CancellationToken cancellationToken); }
internal interface ILoadLsbPayloadUseCase { Task<LsbPayload> ExecuteAsync(string path, CancellationToken cancellationToken); }
internal interface IInspectLsbPixelUseCase { LsbPixelProbe Execute(LsbExperimentSession session, int x, int y); }

internal sealed record LsbReportModel(
    int SchemaVersion,
    int Width,
    int Height,
    int OpaquePixels,
    int EligibleSlots,
    string RecipeId,
    int BitPlane,
    ulong Seed,
    string SeedMeaning,
    int FrameBytes,
    long ChangedSlots,
    long UnchangedSlots,
    double MseRgb,
    double? PsnrRgbDb,
    string Scope,
    long Samples,
    double? CoverOneRatio,
    double? StegoOneRatio,
    double CoverChiSquare,
    double StegoChiSquare,
    double? CoverPValue,
    double? StegoPValue,
    string FrameStatus,
    string? FragilityPreset,
    double? FrameBer,
    IReadOnlyDictionary<string, LsbChannelReportModel> Channels,
    string Notice);

internal sealed record LsbChannelReportModel(long Samples, double? CoverOneRatio, double? StegoOneRatio, double CoverChiSquare, double StegoChiSquare, double? CoverPValue, double? StegoPValue, double? CoverHorizontalTransition, double? StegoHorizontalTransition, double? CoverVerticalTransition, double? StegoVerticalTransition);
