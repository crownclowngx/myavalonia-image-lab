using System.Buffers.Binary;
using System.Security.Cryptography;

namespace ImageLabPlugin.Infrastructure.Watermarking;

/// <summary>把密码学派生的 Mapping Key 转成跨运行稳定的 Fisher-Yates 排列。</summary>
internal static class DeterministicPermutation
{
    public static void Shuffle<T>(Span<T> values, ReadOnlySpan<byte> key)
    {
        if (key.IsEmpty)
        {
            throw new ArgumentException("Mapping Key 不能为空。", nameof(key));
        }

        var hash = SHA256.HashData(key);
        var state = BinaryPrimitives.ReadUInt64LittleEndian(hash);
        for (var i = values.Length - 1; i > 0; i--)
        {
            var random = Next(ref state);
            var swapIndex = (int)(random % (ulong)(i + 1));
            (values[i], values[swapIndex]) = (values[swapIndex], values[i]);
        }
    }

    private static ulong Next(ref ulong state)
    {
        // SplitMix64 的位级定义固定，适合作为确定性排列生成器；安全性来自外部 Mapping Key。
        state += 0x9E3779B97F4A7C15UL;
        var value = state;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }
}
