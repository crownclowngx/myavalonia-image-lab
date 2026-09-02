using System.Numerics;
using ImageLabPlugin.Domain.Shared.Spectral;
using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Domain.MagnitudePhaseSwap;

/// <summary>保存 IFFT 后仍处于原亮度量纲的实数结果及残差事实。</summary>
internal sealed class MagnitudePhaseRawResult
{
    private readonly double[] _values;

    public MagnitudePhaseRawResult(int size, ReadOnlySpan<double> values, double maximumImaginary,
        double relativeImaginary)
    {
        MagnitudePhaseCanvasSize.Validate(size);
        if (values.Length != checked(size * size)) throw new ArgumentException("raw 缓冲长度与画布不一致。", nameof(values));
        if (!double.IsFinite(maximumImaginary) || !double.IsFinite(relativeImaginary) ||
            maximumImaginary < 0d || relativeImaginary < 0d)
            throw new ArgumentOutOfRangeException(nameof(maximumImaginary));
        Size = size;
        MaximumImaginaryResidual = maximumImaginary;
        RelativeImaginaryResidual = relativeImaginary;
        _values = values.ToArray();
    }

    public int Size { get; }
    public double MaximumImaginaryResidual { get; }
    public double RelativeImaginaryResidual { get; }
    public ReadOnlyMemory<double> Values => _values;
}

/// <summary>集中执行 IFFT、有限值检查与相对虚部门禁。</summary>
/// <remarks>
/// 输入必须是混合器新建且由调用方独占的工作频谱，因为共享二维 IFFT 会原地消费它。二维逆变换已在行、列
/// 分别除以长度，最终归一化为 1/(N×N)。绝不能静默丢弃虚部；绝对残差由公共 IFFT 门禁检查，本类再记录
/// 相对实部峰值的残差，使报告能区分亮度尺度不同的实验。
/// </remarks>
internal sealed class MagnitudePhaseReconstructor(FrequencyInverseTransformer inverse)
{
    public MagnitudePhaseRawResult Reconstruct(Complex[] ownedSpectrum, int size,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ownedSpectrum);
        MagnitudePhaseCanvasSize.Validate(size);
        var padded = inverse.InverseOwned(ownedSpectrum, size, size, cancellationToken);
        var values = inverse.Crop(padded, new ImageSize(size, size), cancellationToken);
        double maximumReal = 0d;
        foreach (var value in values)
        {
            if (!double.IsFinite(value)) throw new InvalidDataException("IFFT 实部包含非有限值。");
            maximumReal = Math.Max(maximumReal, Math.Abs(value));
        }
        return new MagnitudePhaseRawResult(size, values, padded.MaximumImaginaryResidual,
            padded.MaximumImaginaryResidual / Math.Max(1d, maximumReal));
    }
}
