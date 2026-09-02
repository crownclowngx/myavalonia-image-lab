using System.Diagnostics;
using System.Buffers.Binary;
using System.Security.Cryptography;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Domain.SvdDecomposition;

namespace ImageLabPlugin.Application.SvdDecomposition;

/// <summary>解码一次源图并建立最大边 128/256 的不可变分析代理，不自动执行 SVD。</summary>
internal sealed class PrepareSvdSessionUseCase(IImageCodec codec, ImageAreaResampler resampler)
    : IPrepareSvdSessionUseCase
{
    internal static readonly int[] SupportedMaximumEdges = [128, 256];

    public async Task<SvdSession> ExecuteAsync(string sourcePath, int analysisMaximumEdge,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        if (!SupportedMaximumEdges.Contains(analysisMaximumEdge))
            throw new ArgumentOutOfRangeException(nameof(analysisMaximumEdge), "SVD 分析档位只能是 128 或 256。");
        var source = await codec.DecodeAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        var proxy = await Task.Run(
            () => resampler.ResizeToMaximumEdge(source, analysisMaximumEdge, cancellationToken),
            cancellationToken).ConfigureAwait(false);
        // 尺寸是矩阵轴向的一部分；仅散列像素字节会让 2×3 与 3×2 的相同字节序列碰到同一缓存身份。
        Span<byte> dimensions = stackalloc byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(dimensions, proxy.Size.Width);
        BinaryPrimitives.WriteInt32LittleEndian(dimensions[4..], proxy.Size.Height);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(dimensions);
        hash.AppendData(proxy.Rgba.Span);
        var fingerprint = Convert.ToHexString(hash.GetHashAndReset());
        return new(sourcePath, source, proxy, analysisMaximumEdge, fingerprint);
    }
}

/// <summary>按固定策略执行有界分解，并只缓存收敛成功的结果。</summary>
internal sealed class DecomposeSvdUseCase(SvdColorStrategyExecutor executor) : IDecomposeSvdUseCase
{
    public Task<SvdDecompositionSet> ExecuteAsync(SvdSession session, SvdColorStrategy strategy,
        ImageChannel singleChannel, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.ThrowIfDisposed();
        var key = new SvdDecompositionCacheKey(session.ProxyFingerprint, strategy, singleChannel,
            SvdRecipeFingerprint.NumericProtocol);
        if (session.TryGet(key, out var cached)) return Task.FromResult(cached);
        return ExecuteCoreAsync(session, key, strategy, singleChannel, cancellationToken);
    }

    private async Task<SvdDecompositionSet> ExecuteCoreAsync(SvdSession session, SvdDecompositionCacheKey key,
        SvdColorStrategy strategy, ImageChannel singleChannel, CancellationToken cancellationToken)
    {
        var result = await Task.Run(() => executor.Decompose(session.AnalysisProxy, session.ProxyFingerprint,
            strategy, singleChannel, cancellationToken), cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        session.Add(key, result);
        return result;
    }
}

/// <summary>从缓存因子执行 Rank-k、颜色合成和质量统计，不重新分解。</summary>
internal sealed class ReconstructSvdRankUseCase(
    LowRankReconstructor reconstructor,
    SvdImageReconstructor imageReconstructor,
    SvdReconstructionAnalyzer analyzer) : IReconstructSvdRankUseCase
{
    public Task<SvdRankResult> ExecuteAsync(SvdSession session, SvdDecompositionSet decomposition,
        int rank, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(decomposition);
        session.ThrowIfDisposed();
        if (!StringComparer.Ordinal.Equals(session.ProxyFingerprint, decomposition.ProxyFingerprint))
            throw new InvalidOperationException("分解结果不属于当前分析代理，请重新分解。");
        if (decomposition.Channels.Count == 0 || decomposition.Channels.Any(item => rank < 0 || rank > item.Factors.RankLimit))
            throw new ArgumentOutOfRangeException(nameof(rank), rank, "Rank 超出当前分解的代数上限。");
        return Task.Run(() =>
        {
            var stopwatch = Stopwatch.StartNew();
            var matrices = decomposition.Channels.Select(item =>
                reconstructor.Reconstruct(item.Factors, rank, cancellationToken)).ToArray();
            var image = imageReconstructor.Reconstruct(session.AnalysisProxy, decomposition, matrices, cancellationToken);
            var diagnostics = analyzer.Analyze(session.AnalysisProxy, image.Image, decomposition, matrices, rank, cancellationToken);
            var fingerprint = SvdRecipeFingerprint.Create(session.ProxyFingerprint, decomposition.Strategy,
                decomposition.SingleChannel, rank);
            return new SvdRankResult(decomposition.Strategy, decomposition.SingleChannel, rank, fingerprint,
                image.Image, matrices, diagnostics.Errors, diagnostics.Quality, image.Clipping,
                diagnostics.AggregateEnergy, stopwatch.Elapsed);
        }, cancellationToken);
    }
}

internal sealed class ProjectSvdComponentUseCase(SvdComponentProjector projector)
    : IProjectSvdComponentUseCase
{
    public Task<SvdComponentProjection> ExecuteAsync(SvdDecompositionSet decomposition,
        int channelIndex, int componentIndex, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(decomposition);
        if ((uint)channelIndex >= (uint)decomposition.Channels.Count)
            throw new ArgumentOutOfRangeException(nameof(channelIndex));
        return Task.Run(() => projector.Project(decomposition.Channels[channelIndex], componentIndex,
            cancellationToken), cancellationToken);
    }
}

/// <summary>按 Y、RGB、YCbCr 固定顺序串行比较；取消时只返回已完成的有序案例。</summary>
internal sealed class CompareSvdStrategiesUseCase(
    IDecomposeSvdUseCase decompose,
    IReconstructSvdRankUseCase reconstruct) : ICompareSvdStrategiesUseCase
{
    public async Task<SvdStrategyComparison> ExecuteAsync(SvdSession session, int rank,
        CancellationToken cancellationToken)
    {
        var plans = new[]
        {
            (SvdColorStrategy.SingleChannel, ImageChannel.Luma),
            (SvdColorStrategy.IndependentRgb, ImageChannel.Luma),
            (SvdColorStrategy.IndependentYCbCr, ImageChannel.Luma)
        };
        var cases = new List<SvdStrategyCase>(plans.Length);
        try
        {
            foreach (var plan in plans)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var factors = await decompose.ExecuteAsync(session, plan.Item1, plan.Item2, cancellationToken).ConfigureAwait(false);
                if (factors.Channels.Any(channel => rank > channel.Factors.RankLimit))
                    throw new ArgumentOutOfRangeException(nameof(rank), rank, "共同 Rank 超出分析代理的代数上限。");
                var result = await reconstruct.ExecuteAsync(session, factors, rank, cancellationToken).ConfigureAwait(false);
                cases.Add(new(plan.Item1, factors.Channels.Count, rank, result.AggregateRetainedEnergy,
                    result.Quality, factors.Elapsed + result.Elapsed));
            }
            return new(rank, cases, SvdComparisonCompletionStatus.Complete);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(rank, cases, SvdComparisonCompletionStatus.CancelledPartial);
        }
    }
}

/// <summary>只导出当前配方指纹一致的分析代理 PNG，并拒绝覆盖源图片。</summary>
internal sealed class ExportSvdImageUseCase(IImageCodec codec, IAtomicFileWriter writer) : IExportSvdImageUseCase
{
    public async Task ExecuteAsync(SvdSession session, SvdRankResult result, string expectedFingerprint,
        string outputPath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        session.ThrowIfDisposed();
        if (!StringComparer.Ordinal.Equals(result.RecipeFingerprint, expectedFingerprint))
            throw new InvalidOperationException("Rank 结果已过期，禁止导出旧分析代理。");
        if (!Path.GetExtension(outputPath).Equals(".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("SVD 重建只允许导出 PNG 分析代理。");
        if (Path.GetFullPath(session.SourcePath).Equals(Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("分析代理不能覆盖源图片。");
        var bytes = await codec.EncodeAsync(result.Image, ImageOutputFormat.Png, 100, cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync(outputPath, bytes, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class ExportSvdReportUseCase(ISvdReportSerializer serializer, IAtomicFileWriter writer)
    : IExportSvdReportUseCase
{
    public Task ExecuteAsync(SvdExperimentReport report, string outputPath, bool csv,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        var bytes = csv ? serializer.SerializeCsv(report) : serializer.SerializeJson(report);
        return writer.WriteAsync(outputPath, bytes, cancellationToken);
    }
}
