namespace ImageLabPlugin.Domain.Frequency;

/// <summary>只计算 32×32 输入左上 8×8 的正交 DCT-II 系数。</summary>
/// <remarks>按频率行检查取消；不分配无用的 32×32 频率缓存，也不对输入减 128。</remarks>
internal sealed class LowFrequencyDctTransform
{
    public const int InputSize = 32;
    public const int OutputSize = 8;
    private readonly OrthogonalDctBasis _basis = new(InputSize);

    public double[] Transform(ReadOnlySpan<double> spatial, CancellationToken cancellationToken = default)
    {
        if (spatial.Length != InputSize * InputSize) throw new ArgumentException("pHash DCT 输入必须恰好为 32×32。", nameof(spatial));
        var result = new double[OutputSize * OutputSize];
        for (var v = 0; v < OutputSize; v++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var u = 0; u < OutputSize; u++)
            {
                double sum = 0d;
                for (var y = 0; y < InputSize; y++)
                for (var x = 0; x < InputSize; x++)
                    sum += spatial[(y * InputSize) + x] * _basis.Cosine(x, u) * _basis.Cosine(y, v);
                result[(v * OutputSize) + u] = _basis.Scale(u) * _basis.Scale(v) * sum;
            }
        }
        return result;
    }
}
