using ImageLabPlugin.Domain.Fingerprinting;
using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Application.Fingerprinting;

internal enum FingerprintStabilityKind { Scale, Jpeg, Brightness, CenterCrop }

/// <summary>固定单轴稳定性配方；去重后最多 21 点，禁止扩展为自由扰动链。</summary>
internal sealed record FingerprintStabilityRecipe
{
    public FingerprintStabilityRecipe(FingerprintStabilityKind kind, IReadOnlyList<decimal> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        Values = values.Distinct().Order().ToArray();
        if (Values.Count is 0 or > 21) throw new ArgumentException("稳定性试验必须包含 1–21 个不同强度点。", nameof(values));
        foreach (var value in Values) Validate(kind, value);
        Kind = kind;
    }

    public FingerprintStabilityKind Kind { get; }
    public IReadOnlyList<decimal> Values { get; }

    private static void Validate(FingerprintStabilityKind kind, decimal value)
    {
        var valid = kind switch
        {
            FingerprintStabilityKind.Scale => value is >= 0.1m and <= 1m,
            FingerprintStabilityKind.Jpeg => value is >= 40m and <= 100m && decimal.Truncate(value) == value,
            FingerprintStabilityKind.Brightness => value is >= -20m and <= 20m,
            FingerprintStabilityKind.CenterCrop => value is >= 0m and <= 10m,
            _ => false
        };
        if (!valid) throw new ArgumentOutOfRangeException(nameof(value), value, $"{kind} 强度超出 V1 固定范围。");
    }
}

internal sealed record FingerprintStabilitySample(PixelImage Image, long? JpegEncodedBytes);

/// <summary>基础设施只暴露四种冻结扰动，不把任意参数字典或鲁棒性配方泄漏给指纹应用层。</summary>
internal interface IFingerprintStabilityChannel
{
    ValueTask<FingerprintStabilitySample> ApplyAsync(PixelImage source, FingerprintStabilityKind kind, decimal value, CancellationToken cancellationToken);
}

internal sealed record FingerprintStabilityAlgorithmPoint(
    FingerprintAlgorithmId AlgorithmId,
    ImageFingerprint Fingerprint,
    FingerprintDistance Distance,
    FingerprintDecision Decision);

internal sealed record FingerprintStabilityPoint(
    decimal RequestedValue,
    ImageSize OutputSize,
    IReadOnlyList<FingerprintStabilityAlgorithmPoint> Algorithms,
    long? JpegEncodedBytes,
    string? Error = null);

internal sealed record FingerprintStabilityProgress(int CompletedPoints, int TotalPoints, decimal? CurrentValue);

internal sealed record FingerprintStabilityResult(
    FingerprintStabilityRecipe Recipe,
    bool IsComplete,
    IReadOnlyList<FingerprintStabilityPoint> Points,
    PixelImage? CurrentSamplePreview,
    string Notice);

internal interface IRunFingerprintStabilityUseCase
{
    Task<FingerprintStabilityResult> ExecuteAsync(
        FingerprintComparisonSession baseline,
        FingerprintStabilityRecipe recipe,
        IProgress<FingerprintStabilityProgress>? progress,
        CancellationToken cancellationToken);
}

/// <summary>串行执行固定稳定性试验；只保留当前样本预览，不缓存 21 张完整扰动图。</summary>
internal sealed class RunFingerprintStabilityUseCase(
    IFingerprintStabilityChannel channel,
    IEnumerable<IImageFingerprintAlgorithm> algorithms,
    FingerprintDistanceCalculator distanceCalculator,
    FingerprintDecisionPolicy policy,
    ImageAnalysisProxyProjector proxyProjector) : IRunFingerprintStabilityUseCase
{
    private readonly IImageFingerprintAlgorithm[] _algorithms = algorithms.ToArray();

    public async Task<FingerprintStabilityResult> ExecuteAsync(
        FingerprintComparisonSession baseline,
        FingerprintStabilityRecipe recipe,
        IProgress<FingerprintStabilityProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(recipe);
        baseline.ThrowIfDisposed();
        var referenceById = baseline.Summary.Algorithms.ToDictionary(value => value.AlgorithmId, value => value.Reference);
        var points = new List<FingerprintStabilityPoint>(recipe.Values.Count);
        PixelImage? currentPreview = null;
        try
        {
            foreach (var value in recipe.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new(points.Count, recipe.Values.Count, value));
                try
                {
                    var sample = await channel.ApplyAsync(baseline.ReferenceImage, recipe.Kind, value, cancellationToken).ConfigureAwait(false);
                    var results = new List<FingerprintStabilityAlgorithmPoint>(_algorithms.Length);
                    foreach (var algorithm in _algorithms)
                    {
                        var fingerprint = algorithm.Compute(sample.Image, cancellationToken);
                        var distance = distanceCalculator.Calculate(referenceById[algorithm.Id], fingerprint);
                        results.Add(new(algorithm.Id, fingerprint, distance, policy.Decide(algorithm.Id, distance)));
                    }
                    currentPreview = proxyProjector.Create(sample.Image, 1024, cancellationToken);
                    points.Add(new(value, sample.Image.Size, results, sample.JpegEncodedBytes));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception exception)
                {
                    points.Add(new(value, baseline.ReferenceImage.Size, [], null, exception.Message));
                }
            }
            progress?.Report(new(points.Count, recipe.Values.Count, null));
            return new(recipe, true, points, currentPreview, Notice(recipe.Kind));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(recipe, false, points, currentPreview, Notice(recipe.Kind));
        }
    }

    private static string Notice(FingerprintStabilityKind kind) => kind == FingerprintStabilityKind.CenterCrop
        ? "中心裁剪试验不做几何配准；距离上升是预期现象。"
        : "曲线描述当前参考图在单一受控扰动下的指纹变化，不代表普适概率。";
}
