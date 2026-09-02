namespace ImageLabPlugin.Domain.Shared.Spatial;

internal enum BorderMode { Constant, Replicate, Reflect101, Wrap }
internal enum KernelNormalizationMode { None, KernelSum, AbsoluteSum, Explicit }

/// <summary>不可变二维卷积核；矩阵锚点固定为中心。</summary>
/// <remarks>
/// 矩阵行列不是采样坐标：第 <c>(row,column)</c> 项表示
/// <c>ky=row-Radius</c>、<c>kx=column-Radius</c>。真卷积会读取
/// <c>f(x-kx,y-ky)</c>，因此非对称核的冲激响应方向与矩阵展示方向一致。
/// 构造和只读属性均复制数组，调用方无法在校验后偷偷改变核事实。
/// </remarks>
internal sealed class ConvolutionKernel
{
    public const int MinimumSize = 3;
    public const int MaximumSize = 31;
    public const double MaximumCoefficientMagnitude = 1024d;
    private readonly double[] _coefficients;

    public ConvolutionKernel(int size, ReadOnlySpan<double> coefficients)
    {
        if (size is < MinimumSize or > MaximumSize || (size & 1) == 0)
            throw new ArgumentOutOfRangeException(nameof(size), size, "卷积核尺寸必须是 3 至 31 的奇数。");
        if (coefficients.Length != checked(size * size))
            throw new ArgumentException($"{size}×{size} 卷积核需要 {size * size} 个系数。", nameof(coefficients));
        for (var index = 0; index < coefficients.Length; index++)
        {
            var value = coefficients[index];
            if (!double.IsFinite(value) || Math.Abs(value) > MaximumCoefficientMagnitude)
                throw new ArgumentOutOfRangeException(nameof(coefficients), $"第 {index + 1} 个系数必须有限且绝对值不超过 {MaximumCoefficientMagnitude}。");
        }
        Size = size;
        Radius = size / 2;
        _coefficients = coefficients.ToArray();
    }

    public int Size { get; }
    public int Radius { get; }
    public ReadOnlyMemory<double> Coefficients => new((double[])_coefficients.Clone());
    internal ReadOnlySpan<double> CoefficientSpan => _coefficients;
    public double Sum => _coefficients.Sum();
    public double AbsoluteSum => _coefficients.Sum(Math.Abs);
    public double this[int row, int column]
    {
        get
        {
            if ((uint)row >= (uint)Size || (uint)column >= (uint)Size)
                throw new ArgumentOutOfRangeException(nameof(row), $"核坐标 ({row},{column}) 超出 {Size}×{Size}。");
            return _coefficients[(row * Size) + column];
        }
    }

    public ConvolutionKernel RotateClockwise()
    {
        var result = new double[_coefficients.Length];
        for (var row = 0; row < Size; row++)
            for (var column = 0; column < Size; column++)
                result[(column * Size) + (Size - 1 - row)] = this[row, column];
        return new ConvolutionKernel(Size, result);
    }
}

internal sealed record BorderDefinition(BorderMode Mode, double ConstantValue = 0d)
{
    public void Validate()
    {
        if (!double.IsFinite(ConstantValue) || ConstantValue is < -1024d or > 1024d)
            throw new ArgumentOutOfRangeException(nameof(ConstantValue), "常量边界值必须有限且位于 -1024 至 1024。");
    }
}

internal sealed record KernelNormalizationDefinition(KernelNormalizationMode Mode, double ExplicitDivisor = 1d);

internal sealed record ConvolutionStatistics(
    double RawMinimum,
    double RawMaximum,
    double BiasedMinimum,
    double BiasedMaximum,
    long LowClippedSamples,
    long HighClippedSamples)
{
    public static ConvolutionStatistics Empty => new(0, 0, 0, 0, 0, 0);
}

internal sealed class ConvolutionPlaneResult
{
    private readonly double[] _rawValues;
    private readonly byte[] _bytes;
    public ConvolutionPlaneResult(int width, int height, ReadOnlySpan<double> rawValues, ReadOnlySpan<byte> bytes,
        double divisor, ConvolutionStatistics statistics)
    {
        if (rawValues.Length != checked(width * height) || bytes.Length != rawValues.Length)
            throw new ArgumentException("卷积结果缓冲长度与尺寸不一致。");
        Width = width; Height = height; Divisor = divisor; Statistics = statistics;
        _rawValues = rawValues.ToArray(); _bytes = bytes.ToArray();
    }
    public int Width { get; }
    public int Height { get; }
    public double Divisor { get; }
    public ConvolutionStatistics Statistics { get; }
    public ReadOnlyMemory<double> RawValues => _rawValues;
    public ReadOnlyMemory<byte> Bytes => _bytes;
    public double this[int x, int y] => _rawValues[(y * Width) + x];
}

internal static class ConvolutionNormalizer
{
    private const double MinimumDivisorMagnitude = 1e-12;

    /// <summary>
    /// 近零除数会改变算子含义且放大浮点误差，所以必须在执行前报告错误；这里绝不静默退回 1。
    /// </summary>
    public static double ResolveDivisor(ConvolutionKernel kernel, KernelNormalizationDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(kernel); ArgumentNullException.ThrowIfNull(definition);
        var divisor = definition.Mode switch
        {
            KernelNormalizationMode.None => 1d,
            KernelNormalizationMode.KernelSum => kernel.Sum,
            KernelNormalizationMode.AbsoluteSum => kernel.AbsoluteSum,
            KernelNormalizationMode.Explicit => definition.ExplicitDivisor,
            _ => throw new ArgumentOutOfRangeException(nameof(definition), "未知归一化模式。")
        };
        if (!double.IsFinite(divisor) || Math.Abs(divisor) < MinimumDivisorMagnitude || Math.Abs(divisor) > 1e12)
            throw new ArgumentOutOfRangeException(nameof(definition), "有效除数必须有限、绝对值至少为 1e-12 且不超过 1e12。");
        return divisor;
    }
}
