using System.Numerics;
using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Domain.Shared.Spectral;

/// <summary>拥有一次完整补零网格 IFFT 的只读实数结果。</summary>
internal sealed class PaddedInverseFrequencyPlane
{
    private readonly double[] _values;

    public PaddedInverseFrequencyPlane(
        int width,
        int height,
        ReadOnlySpan<double> values,
        double maximumImaginaryResidual)
    {
        if (width <= 0 || height <= 0 || values.Length != checked(width * height))
            throw new ArgumentException("padded IFFT 平面尺寸不一致。", nameof(values));
        if (!double.IsFinite(maximumImaginaryResidual) || maximumImaginaryResidual < 0d)
            throw new ArgumentOutOfRangeException(nameof(maximumImaginaryResidual));
        Width = width;
        Height = height;
        MaximumImaginaryResidual = maximumImaginaryResidual;
        _values = values.ToArray();
    }

    public int Width { get; }
    public int Height { get; }
    public double MaximumImaginaryResidual { get; }
    internal ReadOnlySpan<double> ValueSpan => _values;
}

/// <summary>集中执行 IFFT、有限值检查、虚部门禁与源尺寸裁回。</summary>
/// <remarks>
/// 调用方必须传入自己独占的工作频谱；本服务会原地执行 IFFT，因此绝不能传入 Session 中缓存的只读频谱数组。
/// 该所有权约定使 Spectral Art 可以在“源频谱 + 一个工作副本”的预算内完成幅度写入和重建，也让既有频域滤波
/// 继续共享完全相同的归一化、虚部检查和左上角 crop 语义。虚部超过 1E-8 表示共轭不变量已失效，必须失败，
/// 不能通过静默丢弃虚部制造看似可用的图片。
/// </remarks>
internal sealed class FrequencyInverseTransformer(Fft2DTransform fft)
{
    public const double MaximumAllowedImaginaryResidual = 1e-8d;

    public PaddedInverseFrequencyPlane InverseOwned(
        Complex[] ownedWorkingSpectrum,
        int paddedWidth,
        int paddedHeight,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ownedWorkingSpectrum);
        if (ownedWorkingSpectrum.Length != checked(paddedWidth * paddedHeight))
            throw new ArgumentException("工作频谱长度与补零尺寸不一致。", nameof(ownedWorkingSpectrum));

        fft.Inverse(ownedWorkingSpectrum, paddedWidth, paddedHeight, cancellationToken);
        var raw = new double[ownedWorkingSpectrum.Length];
        double maximumImaginary = 0d;
        for (var y = 0; y < paddedHeight; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < paddedWidth; x++)
            {
                var value = ownedWorkingSpectrum[(y * paddedWidth) + x];
                if (!double.IsFinite(value.Real) || !double.IsFinite(value.Imaginary))
                    throw new InvalidDataException("IFFT 产生了非有限结果，未提交半成品。");
                maximumImaginary = Math.Max(maximumImaginary, Math.Abs(value.Imaginary));
                raw[(y * paddedWidth) + x] = value.Real;
            }
        }

        if (maximumImaginary > MaximumAllowedImaginaryResidual)
            throw new InvalidDataException(
                $"IFFT 虚部残差 {maximumImaginary:E3} 超出 {MaximumAllowedImaginaryResidual:E1} 数值门禁。");
        return new PaddedInverseFrequencyPlane(paddedWidth, paddedHeight, raw, maximumImaginary);
    }

    public double[] Crop(
        PaddedInverseFrequencyPlane padded,
        ImageSize sourceSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(padded);
        return Crop(padded.ValueSpan, padded.Width, padded.Height, sourceSize, cancellationToken);
    }

    internal double[] Crop(
        ReadOnlySpan<double> paddedValues,
        int paddedWidth,
        int paddedHeight,
        ImageSize sourceSize,
        CancellationToken cancellationToken = default)
    {
        if (paddedValues.Length != checked(paddedWidth * paddedHeight))
            throw new ArgumentException("补零平面长度与尺寸不一致。", nameof(paddedValues));
        if (sourceSize.Width > paddedWidth || sourceSize.Height > paddedHeight)
            throw new ArgumentException("源尺寸不能超过补零 IFFT 平面。", nameof(sourceSize));
        var cropped = new double[checked((int)sourceSize.PixelCount)];
        for (var y = 0; y < sourceSize.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            paddedValues.Slice(y * paddedWidth, sourceSize.Width)
                .CopyTo(cropped.AsSpan(y * sourceSize.Width, sourceSize.Width));
        }

        return cropped;
    }
}
