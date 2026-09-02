using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.Shared.Analysis;
using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Domain.Robustness;
using ImageLabPlugin.Domain.Shared.Perturbations;
using ImageLabPlugin.Domain.Steganography;
using ImageLabPlugin.Infrastructure.Steganography;

namespace ImageLabPlugin.Application.LsbSteganography;

internal sealed class PrepareLsbExperimentUseCase(IImageCodec codec, LsbCapacityCalculator capacity) : IPrepareLsbExperimentUseCase
{
    public async Task<LsbPreparedSession> ExecuteAsync(string sourcePath, LsbRecipe recipe, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var image = await codec.DecodeAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        var layout = await Task.Run(() => new LsbSlotLayout(image), cancellationToken).ConfigureAwait(false);
        var session = new LsbExperimentSession(sourcePath, image, layout);
        return new(session, capacity.Calculate(image.Size, layout.OpaquePixelCount, recipe, 0));
    }
}

internal sealed class EstimateLsbCapacityUseCase(LsbCapacityCalculator calculator) : IEstimateLsbCapacityUseCase
{
    public LsbCapacity Execute(LsbExperimentSession session, LsbRecipe recipe, int payloadLength)
    {
        session.ThrowIfDisposed();
        return calculator.Calculate(session.SourceImage.Size, session.Layout.OpaquePixelCount, recipe, payloadLength);
    }
}

/// <summary>一次协调 Frame、容量、写入、双重自检、统计与有界预览，不包含任何像素公式。</summary>
internal sealed class EmbedAndAnalyzeLsbUseCase(
    LsbFrameCodec frameCodec,
    LsbCapacityCalculator capacity,
    LsbEmbeddingEngine embedding,
    LsbExtractionEngine extraction,
    LsbStatisticsAnalyzer statistics,
    LsbPreviewProjector projector,
    IImageCodec imageCodec) : IEmbedAndAnalyzeLsbUseCase
{
    public async Task<LsbEmbedUseCaseResult> ExecuteAsync(LsbExperimentSession session, LsbPayload payload, LsbRecipe recipe, LsbStatisticsScope scope, CancellationToken cancellationToken)
    {
        session.ThrowIfDisposed();
        var estimate = capacity.Calculate(session.SourceImage.Size, session.Layout.OpaquePixelCount, recipe, payload.Bytes.Length);
        if (!estimate.Fits) throw new InvalidOperationException($"容量不足：需要 {estimate.RequiredBits:N0} bit，可用 {estimate.EligibleSlots:N0} 槽位。");
        var frame = frameCodec.Encode(payload);
        try
        {
            var embedded = await Task.Run(() => embedding.Embed(session.SourceImage, session.Layout, frame, recipe, cancellationToken), cancellationToken).ConfigureAwait(false);
            var memoryCheck = await Task.Run(() => extraction.Extract(embedded.Image, session.Layout, recipe, cancellationToken), cancellationToken).ConfigureAwait(false);
            EnsureExact(memoryCheck, frame, "内存 stego");
            var png = await imageCodec.EncodeAsync(embedded.Image, ImageOutputFormat.Png, 100, cancellationToken).ConfigureAwait(false);
            var decoded = await imageCodec.DecodeAsync(png, cancellationToken).ConfigureAwait(false);
            if (decoded.Size != embedded.Image.Size || !decoded.Rgba.Span.SequenceEqual(embedded.Image.Rgba.Span)) throw new InvalidOperationException("PNG 无损 RGBA 回读不一致，结果未提交。");
            var decodedLayout = await Task.Run(() => new LsbSlotLayout(decoded), cancellationToken).ConfigureAwait(false);
            var pngCheck = await Task.Run(() => extraction.Extract(decoded, decodedLayout, recipe, cancellationToken), cancellationToken).ConfigureAwait(false);
            EnsureExact(pngCheck, frame, "PNG 编码后");
            var compared = await Task.Run(() => statistics.Compare(session.SourceImage, embedded.Image, session.Layout, recipe, embedded.Facts.SelectedLogicalSlots, scope, cancellationToken), cancellationToken).ConfigureAwait(false);
            var preview = await Task.Run(() => projector.Project(session.SourceImage, embedded.Image, session.Layout, recipe, embedded.Facts, 1024, cancellationToken), cancellationToken).ConfigureAwait(false);
            session.CommitEmbedding(frame, recipe, embedded, memoryCheck, compared);
            return new(embedded.Facts, memoryCheck, compared, preview);
        }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(frame); }
    }

    private static void EnsureExact(LsbExtractionResult result, ReadOnlySpan<byte> frame, string boundary)
    {
        if (result.Status != LsbReadStatus.Success || !result.ReadFrame.AsSpan().SequenceEqual(frame))
            throw new InvalidOperationException($"{boundary} 回读自检失败：{result.Status}；禁止导出。");
    }
}

internal sealed class ExtractLsbPayloadUseCase(LsbExtractionEngine extraction) : IExtractLsbPayloadUseCase
{
    public Task<LsbExtractionResult> ExecuteAsync(PixelImage image, LsbRecipe recipe, CancellationToken cancellationToken) =>
        Task.Run(() => extraction.Extract(image, new LsbSlotLayout(image), recipe, cancellationToken), cancellationToken);
}

/// <summary>只暴露文档冻结的八个 allowlist 预设，并复用现有扰动 Strategy。</summary>
/// <remarks>该用例不建立第二套攻击链；每次从同一 stego 基线开始，缩放显式往返原尺寸。</remarks>
internal sealed class RunLsbFragilityUseCase(
    IEnumerable<IImagePerturbationOperator> operators,
    LsbExtractionEngine extraction,
    FullReferenceQualityAnalyzer quality) : IRunLsbFragilityUseCase
{
    private readonly IReadOnlyDictionary<PerturbationKind, IImagePerturbationOperator> _operators = operators.ToDictionary(x => x.Kind);

    public async Task<LsbFragilityResult> ExecuteAsync(LsbExperimentSession session, LsbFragilityPreset preset, CancellationToken cancellationToken)
    {
        session.ThrowIfDisposed();
        var baseline = session.StegoImage ?? throw new InvalidOperationException("请先完成并验证一次 LSB 写入。");
        var recipe = session.Recipe ?? throw new InvalidOperationException("缺少已验证配方。");
        var sourceFrame = session.Frame.ToArray();
        try
        {
            PixelImage attacked;
            if (preset is LsbFragilityPreset.Scale75 or LsbFragilityPreset.Scale50)
            {
                EnsureOpaque(baseline, "缩放往返");
                var factor = preset == LsbFragilityPreset.Scale75 ? 0.75m : 0.5m;
                var reduced = await ApplyAsync(baseline, PerturbationKind.Scale, new ScaleParameters(factor, factor), recipe.Seed, cancellationToken).ConfigureAwait(false);
                var x = baseline.Size.Width / (decimal)reduced.Size.Width;
                var y = baseline.Size.Height / (decimal)reduced.Size.Height;
                attacked = await ApplyAsync(reduced, PerturbationKind.Scale, new ScaleParameters(x, y), recipe.Seed, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var (kind, parameters) = ResolvePreset(preset);
                if (kind == PerturbationKind.JpegReencode) EnsureOpaque(baseline, "JPEG");
                attacked = await ApplyAsync(baseline, kind, parameters, recipe.Seed, cancellationToken).ConfigureAwait(false);
            }
            if (attacked.Size != baseline.Size) throw new InvalidOperationException("受控往返没有恢复原始尺寸，无法比较同一槽位。");
            var layout = new LsbSlotLayout(attacked);
            var read = extraction.Extract(attacked, layout, recipe, cancellationToken);
            var ber = LsbBerCalculator.Compare(sourceFrame, read.ReadFrame);
            var metrics = quality.Analyze(baseline, attacked, cancellationToken);
            var result = new LsbFragilityResult(preset, attacked, read, ber.Frame, ber.Header, ber.Payload, metrics.PsnrRgbDb);
            session.CommitFragility(result);
            return result;
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(sourceFrame);
        }
    }

    private async ValueTask<PixelImage> ApplyAsync(PixelImage source, PerturbationKind kind, PerturbationParameters parameters, ulong seed, CancellationToken token)
    {
        if (!_operators.TryGetValue(kind, out var implementation)) throw new InvalidOperationException($"未登记复用扰动：{kind.ToStableId()}");
        return await implementation.ApplyAsync(source, parameters, PerturbationSeedDeriver.ForStandalone(seed, $"lsb-{kind.ToStableId()}", kind), token).ConfigureAwait(false);
    }

    private static (PerturbationKind Kind, PerturbationParameters Parameters) ResolvePreset(LsbFragilityPreset preset) => preset switch
    {
        LsbFragilityPreset.Jpeg95 => (PerturbationKind.JpegReencode, new JpegParameters(95)),
        LsbFragilityPreset.Jpeg80 => (PerturbationKind.JpegReencode, new JpegParameters(80)),
        LsbFragilityPreset.Jpeg60 => (PerturbationKind.JpegReencode, new JpegParameters(60)),
        LsbFragilityPreset.GaussianLight => (PerturbationKind.GaussianBlur, new GaussianBlurParameters(0.6m)),
        LsbFragilityPreset.GaussianMedium => (PerturbationKind.GaussianBlur, new GaussianBlurParameters(1.2m)),
        LsbFragilityPreset.Median3 => (PerturbationKind.MedianBlur, new MedianBlurParameters(3)),
        _ => throw new ArgumentOutOfRangeException(nameof(preset))
    };

    private static void EnsureOpaque(PixelImage image, string name)
    {
        var bytes = image.Rgba.Span;
        for (var offset = 3; offset < bytes.Length; offset += 4)
            if (bytes[offset] != byte.MaxValue) throw new InvalidOperationException($"{name} 预设只允许完全不透明图片，避免 Alpha 语义变化。");
    }
}

internal sealed class ExportLsbImageUseCase(IImageCodec codec, IAtomicFileWriter writer, LsbExtractionEngine extraction) : IExportLsbImageUseCase
{
    public async Task<LsbImageExportResult> ExecuteAsync(LsbExperimentSession session, string outputPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        session.ThrowIfDisposed();
        var stego = session.StegoImage ?? throw new InvalidOperationException("没有可导出的 stego 图片。");
        var recipe = session.Recipe ?? throw new InvalidOperationException("没有可导出的配方。");
        if (!session.HasVerifiedStego) throw new InvalidOperationException("自检未通过，禁止导出。");
        var png = await codec.EncodeAsync(stego, ImageOutputFormat.Png, 100, cancellationToken).ConfigureAwait(false);
        var decoded = await codec.DecodeAsync(png, cancellationToken).ConfigureAwait(false);
        if (!decoded.Rgba.Span.SequenceEqual(stego.Rgba.Span)) throw new InvalidOperationException("导出前 PNG RGBA 回读失败。");
        var check = extraction.Extract(decoded, new LsbSlotLayout(decoded), recipe, cancellationToken);
        if (check.Status != LsbReadStatus.Success || !check.ReadFrame.AsSpan().SequenceEqual(session.Frame.Span)) throw new InvalidOperationException("导出前 Frame 回读失败，未发布文件。");
        await writer.WriteAsync(outputPath, png, cancellationToken).ConfigureAwait(false);
        return new(outputPath, png.Length, check);
    }
}

internal sealed class LoadLsbPayloadUseCase(ILsbPayloadFileReader reader) : ILoadLsbPayloadUseCase
{
    public async Task<LsbPayload> ExecuteAsync(string path, CancellationToken cancellationToken) =>
        new(LsbPayloadKind.Binary, await reader.ReadAsync(path, cancellationToken).ConfigureAwait(false));
}

internal sealed class InspectLsbPixelUseCase(LsbPixelInspector inspector) : IInspectLsbPixelUseCase
{
    public LsbPixelProbe Execute(LsbExperimentSession session, int x, int y)
    {
        session.ThrowIfDisposed();
        if (session.StegoImage is null || session.Recipe is null || session.EmbeddingFacts is null) throw new InvalidOperationException("请先完成当前配方写入再使用探针。");
        return inspector.Inspect(session.SourceImage, session.StegoImage, session.Layout, session.Recipe.Value, session.EmbeddingFacts, x, y);
    }
}

internal sealed class ExportLsbReportUseCase(LsbExperimentReportSerializer serializer, IAtomicFileWriter writer) : IExportLsbReportUseCase
{
    public async Task<LsbReportExportResult> ExecuteAsync(LsbExperimentSession session, string outputPath, string format, CancellationToken cancellationToken)
    {
        session.ThrowIfDisposed();
        if (!session.HasVerifiedStego || session.EmbeddingFacts is null || session.Statistics is null || session.Recipe is null) throw new InvalidOperationException("只有完整自检通过的结果才能导出报告。");
        var bytes = format.Equals("csv", StringComparison.OrdinalIgnoreCase) ? serializer.SerializeCsv(session) : serializer.SerializeJson(session);
        await writer.WriteAsync(outputPath, bytes, cancellationToken).ConfigureAwait(false);
        return new(outputPath, format.ToLowerInvariant(), bytes.Length);
    }
}
