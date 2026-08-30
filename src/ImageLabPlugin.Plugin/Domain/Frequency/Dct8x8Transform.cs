namespace ImageLabPlugin.Domain.Frequency;

/// <summary>实现固定 8×8 的正交二维 DCT-II 与逆变换。</summary>
/// <remarks>
/// 该类无可变状态，可安全复用。预先计算余弦表既减少热循环开销，也避免不同调用路径生成略有不同的常量。
/// 输入亮度在变换前减去 128，和常见图片 DCT 约定保持一致。
/// </remarks>
internal sealed class Dct8x8Transform
{
    public const int BlockSize = 8;
    private static readonly double[,] Cosines = CreateCosines();

    public void Forward(ReadOnlySpan<double> spatial, Span<double> frequency)
    {
        ValidateBuffers(spatial, frequency);
        for (var v = 0; v < BlockSize; v++)
        {
            for (var u = 0; u < BlockSize; u++)
            {
                double sum = 0;
                for (var y = 0; y < BlockSize; y++)
                {
                    for (var x = 0; x < BlockSize; x++)
                    {
                        sum += (spatial[(y * BlockSize) + x] - 128d) * Cosines[x, u] * Cosines[y, v];
                    }
                }

                frequency[(v * BlockSize) + u] = 0.25d * Scale(u) * Scale(v) * sum;
            }
        }
    }

    public void Inverse(ReadOnlySpan<double> frequency, Span<double> spatial)
    {
        ValidateBuffers(frequency, spatial);
        for (var y = 0; y < BlockSize; y++)
        {
            for (var x = 0; x < BlockSize; x++)
            {
                double sum = 0;
                for (var v = 0; v < BlockSize; v++)
                {
                    for (var u = 0; u < BlockSize; u++)
                    {
                        sum += Scale(u) * Scale(v) * frequency[(v * BlockSize) + u] *
                            Cosines[x, u] * Cosines[y, v];
                    }
                }

                spatial[(y * BlockSize) + x] = Math.Clamp((0.25d * sum) + 128d, 0d, 255d);
            }
        }
    }

    private static void ValidateBuffers(ReadOnlySpan<double> input, Span<double> output)
    {
        if (input.Length != 64 || output.Length != 64)
        {
            throw new ArgumentException("8×8 DCT 的输入和输出都必须恰好包含 64 个值。");
        }
    }

    private static double Scale(int frequency) => frequency == 0 ? 1d / Math.Sqrt(2d) : 1d;

    private static double[,] CreateCosines()
    {
        var values = new double[BlockSize, BlockSize];
        for (var position = 0; position < BlockSize; position++)
        {
            for (var frequency = 0; frequency < BlockSize; frequency++)
            {
                values[position, frequency] = Math.Cos(((2d * position + 1d) * frequency * Math.PI) / 16d);
            }
        }

        return values;
    }
}
