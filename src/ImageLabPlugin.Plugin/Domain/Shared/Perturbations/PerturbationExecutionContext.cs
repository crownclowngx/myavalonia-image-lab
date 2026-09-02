using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ImageLabPlugin.Domain.Shared.Perturbations;

/// <summary>扰动算子只接收已经派生的私有随机种子，不感知实验或 Capability 模型。</summary>
internal sealed record PerturbationExecutionContext(ulong DerivedSeed, string StepId, PerturbationKind Kind)
{
    public PerturbationRandom CreateRandom() => new(DerivedSeed);
}

/// <summary>冻结的 SplitMix64，仅用于可复现实验，不用于密码学用途。</summary>
internal sealed class PerturbationRandom
{
    private ulong _state;
    public PerturbationRandom(ulong seed) => _state = seed;

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

/// <summary>兼容既有 robustness-rng-v1 canonical 格式的种子派生协议。</summary>
internal static class PerturbationSeedDeriver
{
    private const byte StandaloneBalancedProfileValue = 2;

    public static PerturbationExecutionContext ForStandalone(ulong seed, string stepId, PerturbationKind kind) =>
        FromCanonicalFacts(seed, StandaloneBalancedProfileValue, 0m, 0, stepId, kind);

    public static PerturbationExecutionContext FromCanonicalFacts(
        ulong seed,
        byte profileValue,
        decimal canonicalValue,
        int trialIndex,
        string stepId,
        PerturbationKind kind)
    {
        var canonical = string.Join('|',
            "robustness-rng-v1",
            seed.ToString(CultureInfo.InvariantCulture),
            profileValue.ToString(CultureInfo.InvariantCulture),
            canonicalValue.ToString(CultureInfo.InvariantCulture),
            trialIndex.ToString(CultureInfo.InvariantCulture),
            stepId,
            kind.ToStableId());
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return new(BinaryPrimitives.ReadUInt64LittleEndian(hash), stepId, kind);
    }
}
