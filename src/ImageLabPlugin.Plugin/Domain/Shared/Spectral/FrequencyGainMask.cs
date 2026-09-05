using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ImageLabPlugin.Domain.Shared.Spectral;

/// <summary>拥有一张不可变、有限且共轭对称的二维实数增益遮罩。</summary>
/// <remarks>
/// 这里只接受 <c>[0,1]</c> 的实数增益，因为当前产品只衰减频谱幅值，不编辑相位，也不允许放大。
/// 构造时复制调用方缓冲并验证每个自然索引和其共轭点，使后续 IFFT 可以依赖“实值输出”这一硬不变量。
/// </remarks>
internal sealed class FrequencyGainMask
{
    private const double SymmetryTolerance = 1e-12;
    private readonly double[] _gains;

    public FrequencyGainMask(int width, int height, ReadOnlySpan<double> gains, string? fingerprint = null)
    {
        if (width <= 0 || height <= 0 || width > 2048 || height > 2048 ||
            gains.Length != checked(width * height) || gains.Length > FrequencySpectrum.MaximumComplexValues)
            throw new ArgumentException("增益遮罩尺寸、缓冲长度或资源预算不合法。", nameof(gains));

        Width = width;
        Height = height;
        _gains = gains.ToArray();
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var value = _gains[(y * width) + x];
                if (!double.IsFinite(value) || value is < 0d or > 1d)
                    throw new ArgumentOutOfRangeException(nameof(gains), "增益必须有限且位于 [0,1]。");
                var conjugate = FrequencyCoordinates.ConjugateIndex(x, y, width, height);
                var paired = _gains[(conjugate.Y * width) + conjugate.X];
                if (!double.IsFinite(paired) || Math.Abs(value - paired) > SymmetryTolerance)
                    throw new ArgumentException("增益遮罩不满足自然索引下的共轭对称约束。", nameof(gains));
            }

        Fingerprint = string.IsNullOrWhiteSpace(fingerprint) ? ComputeFingerprint(width, height, _gains) : fingerprint;
    }

    public int Width { get; }
    public int Height { get; }
    public string Fingerprint { get; }
    public ReadOnlyMemory<double> Gains => new((double[])_gains.Clone());
    public double this[int internalX, int internalY] => _gains[(internalY * Width) + internalX];
    internal ReadOnlySpan<double> GainSpan => _gains;

    private static string ComputeFingerprint(int width, int height, ReadOnlySpan<double> gains)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"frequency-gain-mask-v1|{width}|{height}|")));
        foreach (var gain in gains)
            hash.AppendData(BitConverter.GetBytes(BitConverter.DoubleToInt64Bits(gain)));
        return Convert.ToHexString(hash.GetHashAndReset())[..16].ToLowerInvariant();
    }
}
