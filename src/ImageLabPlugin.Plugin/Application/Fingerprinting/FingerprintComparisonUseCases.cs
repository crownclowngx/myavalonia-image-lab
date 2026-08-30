using System.Diagnostics;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.Fingerprinting;
using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Application.Fingerprinting;

/// <summary>顺序协调双图解码、预览、三种算法和比较摘要；不包含任何像素或 DCT 公式。</summary>
internal sealed class PrepareFingerprintComparisonUseCase(
    IImageCodec codec,
    ImageAnalysisProxyProjector proxyProjector,
    IEnumerable<IImageFingerprintAlgorithm> algorithms,
    FingerprintDistanceCalculator distanceCalculator,
    FingerprintDecisionPolicy policy) : IPrepareFingerprintComparisonUseCase
{
    private readonly IImageFingerprintAlgorithm[] _algorithms = ValidateAlgorithms(algorithms);

    public async Task<FingerprintComparisonSession> ExecuteAsync(FingerprintComparisonRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ReferencePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CandidatePath);
        if (request.MaximumDisplayEdge != 1024) throw new ArgumentOutOfRangeException(nameof(request), "V1 预览最大边固定为 1024。");

        // 顺序解码控制编解码临时峰值；不同尺寸是指纹的正常输入，不调用同尺寸比较验证器。
        var reference = await codec.DecodeAsync(request.ReferencePath, cancellationToken).ConfigureAwait(false);
        var candidate = await codec.DecodeAsync(request.CandidatePath, cancellationToken).ConfigureAwait(false);
        return await Task.Run(() => BuildSession(request, reference, candidate, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    private FingerprintComparisonSession BuildSession(FingerprintComparisonRequest request, PixelImage reference, PixelImage candidate, CancellationToken token)
    {
        var referenceProxy = proxyProjector.Create(reference, 1024, token);
        var candidateProxy = proxyProjector.Create(candidate, 1024, token);
        var results = new List<FingerprintAlgorithmResult>(_algorithms.Length);
        foreach (var algorithm in _algorithms)
        {
            token.ThrowIfCancellationRequested();
            var stopwatch = Stopwatch.StartNew();
            var left = algorithm.Compute(reference, token);
            var right = algorithm.Compute(candidate, token);
            stopwatch.Stop();
            var distance = distanceCalculator.Calculate(left, right);
            results.Add(new(algorithm.Id, left, right, distance, policy.GetThreshold(algorithm.Id), policy.Decide(algorithm.Id, distance), stopwatch.Elapsed, GetLimitation(algorithm.Id)));
        }

        var summary = new FingerprintComparisonSummary(
            FingerprintLumaNormalizer.NormalizationId,
            FingerprintDecisionPolicy.PolicyId,
            new(Path.GetFileName(request.ReferencePath), reference.Size, HasAlpha(reference)),
            new(Path.GetFileName(request.CandidatePath), candidate.Size, HasAlpha(candidate)),
            results,
            policy.Summarize(results.Select(value => value.Decision)),
            DateTimeOffset.UtcNow,
            "感知指纹只提供启发式相似线索；位相似度不是来源概率，也不能证明版权、来源或文件相同。"
        );
        return new FingerprintComparisonSession(reference, candidate, referenceProxy, candidateProxy, summary);
    }

    private static bool HasAlpha(PixelImage image)
    {
        var rgba = image.Rgba.Span;
        for (var offset = 3; offset < rgba.Length; offset += 4) if (rgba[offset] != 255) return true;
        return false;
    }

    private static IImageFingerprintAlgorithm[] ValidateAlgorithms(IEnumerable<IImageFingerprintAlgorithm> algorithms)
    {
        ArgumentNullException.ThrowIfNull(algorithms);
        var values = algorithms.ToArray();
        var expected = new[] { FingerprintAlgorithmId.AverageHash, FingerprintAlgorithmId.DifferenceHash, FingerprintAlgorithmId.PerceptualHash };
        if (!values.Select(value => value.Id).SequenceEqual(expected))
            throw new InvalidOperationException("指纹算法必须按 aHash、dHash、pHash 固定顺序各登记一次。");
        return values;
    }

    internal static string GetLimitation(FingerprintAlgorithmId id) => id == FingerprintAlgorithmId.AverageHash
        ? "观察平均亮暗布局；平坦图易碰撞，对裁剪、旋转和镜像敏感。"
        : id == FingerprintAlgorithmId.DifferenceHash
            ? "观察水平亮度梯度；不代表垂直梯度，对裁剪、旋转和镜像敏感。"
            : "观察低频 DCT 结构；不具备裁剪、旋转、镜像或几何配准不变性。";
}
