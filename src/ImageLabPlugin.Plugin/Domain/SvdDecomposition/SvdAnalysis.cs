using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Domain.SvdDecomposition;

/// <summary>从奇异值生成累计能量、数值秩和理论尾能量。</summary>
internal sealed class SingularValueEnergyAnalyzer
{
    private const double RankToleranceFactor = 32d;

    public SingularValueEnergyReport Analyze(SvdFactors factors)
    {
        ArgumentNullException.ThrowIfNull(factors);
        var sigma = factors.SingularValues.Span;
        double total = 0d, compensation = 0d;
        foreach (var value in sigma) JacobiSvdDecomposer.AddKahan(value * value, ref total, ref compensation);
        var tolerance = sigma.Length == 0 ? 0d : Math.Max(factors.Rows, factors.Columns) *
            JacobiSvdDecomposer.MachineEpsilon * sigma[0] * RankToleranceFactor;
        var numericRank = 0;
        foreach (var value in sigma) if (value > tolerance) numericRank++;
        var samples = new SingularValueEnergySample[sigma.Length];
        double cumulative = 0d;
        compensation = 0d;
        for (var index = 0; index < sigma.Length; index++)
        {
            var square = sigma[index] * sigma[index];
            JacobiSvdDecomposer.AddKahan(square, ref cumulative, ref compensation);
            samples[index] = new(index, sigma[index], sigma[0] == 0d ? 0d : sigma[index] / sigma[0],
                total == 0d ? 0d : square / total, total == 0d ? 0d : Math.Clamp(cumulative / total, 0d, 1d));
        }
        return new(total, samples, numericRank, tolerance,
            total == 0d ? SvdEnergyStatus.NotApplicable : SvdEnergyStatus.Available);
    }

    public double TailEnergy(SvdFactors factors, int rank)
    {
        ValidateRank(factors, rank);
        double result = 0d, compensation = 0d;
        var sigma = factors.SingularValues.Span;
        for (var index = rank; index < sigma.Length; index++)
            JacobiSvdDecomposer.AddKahan(sigma[index] * sigma[index], ref result, ref compensation);
        return result;
    }

    internal static void ValidateRank(SvdFactors factors, int rank)
    {
        ArgumentNullException.ThrowIfNull(factors);
        if ((uint)rank > (uint)factors.RankLimit)
            throw new ArgumentOutOfRangeException(nameof(rank), rank, $"Rank 必须位于 0–{factors.RankLimit}。");
    }
}

/// <summary>只从不可变因子计算指定 Rank-k，不修改或缓存任何因子。</summary>
internal sealed class LowRankReconstructor
{
    public DenseMatrix Reconstruct(SvdFactors factors, int rank, CancellationToken cancellationToken = default)
    {
        SingularValueEnergyAnalyzer.ValidateRank(factors, rank);
        var result = new double[checked(factors.Rows * factors.Columns)];
        var sigma = factors.SingularValues.Span;
        for (var row = 0; row < factors.Rows; row++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var column = 0; column < factors.Columns; column++)
            {
                double value = 0d;
                for (var component = 0; component < rank; component++)
                    value += sigma[component] * factors.GetU(row, component) * factors.GetV(column, component);
                result[(row * factors.Columns) + column] = value;
            }
        }
        return DenseMatrix.FromOwned(factors.Rows, factors.Columns, result);
    }
}

/// <summary>按需把一个 σᵢuᵢvᵢᵀ 映射为有符号发散色观察图。</summary>
/// <remarks>
/// raw 分量先完整计算，再以 max(|min|,|max|) 对称归一化：负数偏蓝、零为中性灰、正数偏橙。
/// 显示比例只用于观察，绝不写回 Rank-k 矩阵；否则图片结果会依赖当前选择的分量和色标。
/// </remarks>
internal sealed class SvdComponentProjector
{
    public SvdComponentProjection Project(SvdChannelFactors channel, int componentIndex,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var factors = channel.Factors;
        if ((uint)componentIndex >= (uint)factors.RankLimit)
            throw new ArgumentOutOfRangeException(nameof(componentIndex));
        var sigma = factors.SingularValues.Span[componentIndex];
        var values = new double[checked(factors.Rows * factors.Columns)];
        var minimum = double.PositiveInfinity;
        var maximum = double.NegativeInfinity;
        for (var row = 0; row < factors.Rows; row++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var column = 0; column < factors.Columns; column++)
            {
                var value = sigma * factors.GetU(row, componentIndex) * factors.GetV(column, componentIndex);
                values[(row * factors.Columns) + column] = value;
                minimum = Math.Min(minimum, value);
                maximum = Math.Max(maximum, value);
            }
        }
        var scale = Math.Max(Math.Abs(minimum), Math.Abs(maximum));
        var rgba = new byte[checked(values.Length * 4)];
        for (var index = 0; index < values.Length; index++)
        {
            var normalized = scale == 0d ? 0d : Math.Clamp(values[index] / scale, -1d, 1d);
            var magnitude = Math.Abs(normalized);
            var offset = index * 4;
            if (normalized == 0d)
            {
                rgba[offset] = rgba[offset + 1] = rgba[offset + 2] = 238;
            }
            else if (normalized < 0d)
            {
                rgba[offset] = (byte)Math.Round(238d - (180d * magnitude));
                rgba[offset + 1] = (byte)Math.Round(238d - (95d * magnitude));
                rgba[offset + 2] = 255;
            }
            else
            {
                rgba[offset] = 255;
                rgba[offset + 1] = (byte)Math.Round(238d - (110d * magnitude));
                rgba[offset + 2] = (byte)Math.Round(238d - (200d * magnitude));
            }
            rgba[offset + 3] = 255;
        }
        double total = 0d, compensation = 0d;
        foreach (var value in factors.SingularValues.Span)
            JacobiSvdDecomposer.AddKahan(value * value, ref total, ref compensation);
        return new(channel.Channel, componentIndex, sigma, total == 0d ? null : (sigma * sigma) / total,
            minimum, maximum, scale, new PixelImage(new ImageSize(factors.Columns, factors.Rows), rgba));
    }
}
