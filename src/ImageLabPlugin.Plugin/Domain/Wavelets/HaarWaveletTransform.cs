namespace ImageLabPlugin.Domain.Wavelets;

/// <summary>正交归一化 Haar 策略，使用 double 原地写回 packed 低频/高频半区。</summary>
internal sealed class HaarWaveletTransform : WaveletTransformBase
{
    private static readonly double InverseSqrtTwo = 1d / Math.Sqrt(2d);
    public override WaveletTransformId Id => WaveletTransformId.Haar;

    protected override void Forward1D(Span<double> values, Span<double> workspace)
    {
        // workspace 与 values 不重叠且至少同长；先完整计算再复制，避免前半区写回破坏尚未读取的样本对。
        var half = values.Length / 2;
        for (var i = 0; i < half; i++)
        {
            var first = values[i * 2];
            var second = values[(i * 2) + 1];
            workspace[i] = (first + second) * InverseSqrtTwo;
            workspace[half + i] = (first - second) * InverseSqrtTwo;
        }
        workspace[..values.Length].CopyTo(values);
    }

    protected override void Inverse1D(Span<double> values, Span<double> workspace)
    {
        var target = workspace.Length >= values.Length ? workspace : new double[values.Length];
        var half = values.Length / 2;
        for (var i = 0; i < half; i++)
        {
            var low = values[i];
            var high = values[half + i];
            target[i * 2] = (low + high) * InverseSqrtTwo;
            target[(i * 2) + 1] = (low - high) * InverseSqrtTwo;
        }
        target[..values.Length].CopyTo(values);
    }
}
