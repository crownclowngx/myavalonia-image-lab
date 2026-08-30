using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ImageLabPlugin.Domain.Watermarking;

namespace ImageLabPlugin.Domain.Robustness;

/// <summary>一个步骤私有的确定性随机上下文。</summary>
/// <remarks>
/// 子种子由配方稳定事实经 SHA-256 派生，不能消费水印协议使用的密码学随机源。由此取消、重排案例或增加扫描点时，
/// 其他案例的噪声不会漂移。生成器采用冻结的 SplitMix64；它用于可复现实验，不用于密码、salt、nonce 或密钥。
/// </remarks>
internal sealed class DeterministicTrialRandom
{
    private ulong _state;
    public DeterministicTrialRandom(ulong seed) => _state = seed;
    public ulong NextUInt64()
    {
        var value = _state += 0x9E3779B97F4A7C15UL;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }
    public double NextDouble() => (NextUInt64() >> 11) * (1d / (1UL << 53));
    public int NextInt(int exclusiveMaximum) => exclusiveMaximum > 0
        ? (int)(NextUInt64() % (uint)exclusiveMaximum)
        : throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum));
}

internal sealed record DeterministicTrialContext(ulong Seed, RobustnessCaseKey CaseKey, string StepId, PerturbationKind Kind)
{
    /// <summary>为不属于频域水印 Profile 矩阵的单次受控实验创建稳定上下文。</summary>
    /// <remarks>占位 Profile 只参与既有随机派生格式；调用方无需依赖水印领域，也不会触发水印协议或 Carrier。</remarks>
    public static DeterministicTrialContext ForStandalone(ulong seed, string stepId, PerturbationKind kind) =>
        new(seed, new(EmbeddingProfileId.Balanced, 0, 0m, 0), stepId, kind);

    public DeterministicTrialRandom CreateRandom() => new(DeriveSeed());

    public ulong DeriveSeed()
    {
        var canonical = string.Join('|',
            "robustness-rng-v1",
            Seed.ToString(CultureInfo.InvariantCulture),
            ((byte)CaseKey.Profile).ToString(CultureInfo.InvariantCulture),
            CaseKey.CanonicalValue.ToString(CultureInfo.InvariantCulture),
            CaseKey.TrialIndex.ToString(CultureInfo.InvariantCulture),
            StepId,
            Kind.ToStableId());
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return BinaryPrimitives.ReadUInt64LittleEndian(hash);
    }
}
