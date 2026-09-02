using ImageLabPlugin.Domain.Shared.Spectral;
using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Domain.Fingerprinting;

/// <summary>感知指纹算法唯一的朴素 Strategy 替换点；实现必须无状态且不得修改输入图。</summary>
internal interface IImageFingerprintAlgorithm
{
    FingerprintAlgorithmId Id { get; }
    ImageFingerprint Compute(PixelImage source, CancellationToken cancellationToken = default);
}

internal sealed class AverageHashAlgorithm(FingerprintLumaNormalizer normalizer) : IImageFingerprintAlgorithm
{
    public FingerprintAlgorithmId Id => FingerprintAlgorithmId.AverageHash;

    public ImageFingerprint Compute(PixelImage source, CancellationToken cancellationToken = default)
    {
        var luma = normalizer.Normalize(source, 8, 8, cancellationToken);
        double sum = 0d;
        foreach (var value in luma) sum += value;
        var mean = sum / 64d;
        // “大于等于”属于摘要协议；它使均匀图稳定地产生全 1，而不是依赖浮点偶然误差。
        return new ImageFingerprint(Id, Pack(luma, value => value >= mean));
    }

    internal static ulong Pack(ReadOnlySpan<double> values, Func<double, bool> predicate)
    {
        ulong bits = 0;
        for (var index = 0; index < 64; index++) if (predicate(values[index])) bits |= 1UL << (63 - index);
        return bits;
    }
}

internal sealed class DifferenceHashAlgorithm(FingerprintLumaNormalizer normalizer) : IImageFingerprintAlgorithm
{
    public FingerprintAlgorithmId Id => FingerprintAlgorithmId.DifferenceHash;

    public ImageFingerprint Compute(PixelImage source, CancellationToken cancellationToken = default)
    {
        var luma = normalizer.Normalize(source, 9, 8, cancellationToken);
        ulong bits = 0;
        for (var y = 0; y < 8; y++)
        for (var x = 0; x < 8; x++)
        {
            // 固定 left > right；相等写 0，不能与 aHash 的 >= 规则共用。
            if (luma[(y * 9) + x] > luma[(y * 9) + x + 1]) bits |= 1UL << (63 - ((y * 8) + x));
        }
        return new ImageFingerprint(Id, bits);
    }
}

internal sealed class PerceptualHashAlgorithm(
    FingerprintLumaNormalizer normalizer,
    LowFrequencyDctTransform dct) : IImageFingerprintAlgorithm
{
    public FingerprintAlgorithmId Id => FingerprintAlgorithmId.PerceptualHash;

    public ImageFingerprint Compute(PixelImage source, CancellationToken cancellationToken = default)
    {
        var luma = normalizer.Normalize(source, 32, 32, cancellationToken);
        var coefficients = dct.Transform(luma, cancellationToken);
        var ac = coefficients[1..];
        Array.Sort(ac);
        var median = ac[31];
        // 中位数排除 DC，避免整体亮度支配阈值；输出仍保留 DC 位，从而保持 8×8、64 位行优先协议。
        return new ImageFingerprint(Id, AverageHashAlgorithm.Pack(coefficients, value => value >= median));
    }
}
