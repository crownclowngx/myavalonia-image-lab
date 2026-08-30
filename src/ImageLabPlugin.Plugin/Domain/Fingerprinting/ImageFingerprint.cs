using System.Globalization;
using System.Numerics;

namespace ImageLabPlugin.Domain.Fingerprinting;

/// <summary>一个带稳定算法身份的 64 位感知指纹。</summary>
/// <remarks>位序固定为 8×8 行优先：左上角是 bit 63，右下角是 bit 0。</remarks>
internal readonly record struct ImageFingerprint(FingerprintAlgorithmId AlgorithmId, ulong Bits)
{
    public string ToCanonicalHex() => Bits.ToString("X16", CultureInfo.InvariantCulture);

    public bool GetBit(int x, int y)
    {
        if ((uint)x >= 8u || (uint)y >= 8u) throw new ArgumentOutOfRangeException(nameof(x), "位图坐标必须位于 8×8 范围内。");
        return (Bits & (1UL << (63 - ((y * 8) + x)))) != 0;
    }

    public static ImageFingerprint Parse(FingerprintAlgorithmId algorithmId, string canonicalHex)
    {
        if (canonicalHex is null || canonicalHex.Length != 16 ||
            !ulong.TryParse(canonicalHex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var bits))
        {
            throw new FormatException("规范指纹必须恰好包含 16 个十六进制字符。");
        }

        return new ImageFingerprint(algorithmId, bits);
    }
}

/// <summary>同算法指纹的汉明距离结果；位相似度只是位匹配比例，不是来源概率。</summary>
internal readonly record struct FingerprintDistance(int Distance)
{
    public double BitSimilarityPercent => 100d * (64 - Distance) / 64d;
}

internal sealed class FingerprintDistanceCalculator
{
    public FingerprintDistance Calculate(ImageFingerprint left, ImageFingerprint right)
    {
        if (left.AlgorithmId != right.AlgorithmId)
            throw new ArgumentException($"不同算法不能计算汉明距离：{left.AlgorithmId} 与 {right.AlgorithmId}。");
        return new FingerprintDistance(BitOperations.PopCount(left.Bits ^ right.Bits));
    }
}

/// <summary>鲁棒性实验的只读指纹观测；它不参与水印成功、BER、质量或失败分类。</summary>
internal sealed record FingerprintObservation(
    FingerprintAlgorithmId AlgorithmId,
    ImageFingerprint Reference,
    ImageFingerprint Candidate,
    FingerprintDistance Distance);

/// <summary>把 64 位摘要投影为 UI 可直接读取的 8×8 布尔矩阵，不参与距离计算。</summary>
internal static class FingerprintBitmapProjector
{
    public static bool[,] Project(ImageFingerprint fingerprint)
    {
        var result = new bool[8, 8];
        for (var y = 0; y < 8; y++)
        for (var x = 0; x < 8; x++)
            result[y, x] = fingerprint.GetBit(x, y);
        return result;
    }
}
