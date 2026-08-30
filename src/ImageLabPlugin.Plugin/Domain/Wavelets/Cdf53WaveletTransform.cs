namespace ImageLabPlugin.Domain.Wavelets;

/// <summary>以 predict/update lifting 实现的 CDF 5/3 双正交策略。</summary>
/// <remarks>
/// 偶样本形成低频 s，奇样本形成细节 d。边界缺失邻居复制端点：
/// d[i] -= (s[i] + s[i+1])/2；s[i] += (d[i-1] + d[i])/4。逆变换严格反序撤销。
/// V1 不额外缩放 s/d，因此只承诺正逆重建，不对它套用 Haar Parseval 能量断言。
/// </remarks>
internal sealed class Cdf53WaveletTransform : WaveletTransformBase
{
    public override WaveletTransformId Id => WaveletTransformId.Cdf53;

    protected override void Forward1D(Span<double> values, Span<double> workspace)
    {
        var half = values.Length / 2;
        for (var i = 0; i < half; i++)
        {
            workspace[i] = values[i * 2];
            workspace[half + i] = values[(i * 2) + 1];
        }
        var low = workspace[..half];
        var high = workspace.Slice(half, half);
        for (var i = 0; i < half; i++) high[i] -= (low[i] + low[Math.Min(i + 1, half - 1)]) * 0.5d;
        for (var i = 0; i < half; i++) low[i] += (high[Math.Max(i - 1, 0)] + high[i]) * 0.25d;
        workspace[..values.Length].CopyTo(values);
    }

    protected override void Inverse1D(Span<double> values, Span<double> workspace)
    {
        var target = workspace.Length >= values.Length ? workspace : new double[values.Length];
        values.CopyTo(target);
        var half = values.Length / 2;
        var low = target[..half];
        var high = target.Slice(half, half);
        for (var i = 0; i < half; i++) low[i] -= (high[Math.Max(i - 1, 0)] + high[i]) * 0.25d;
        for (var i = 0; i < half; i++) high[i] += (low[i] + low[Math.Min(i + 1, half - 1)]) * 0.5d;
        for (var i = 0; i < half; i++)
        {
            values[i * 2] = low[i];
            values[(i * 2) + 1] = high[i];
        }
    }
}
