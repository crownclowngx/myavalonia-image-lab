using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Domain.Shared.Spatial;

internal readonly record struct BorderSample(int Index, bool IsConstant);

/// <summary>四种边界的一维规范映射；空间执行器和像素探针必须共用它。</summary>
internal static class BorderIndexMapper
{
    /// <remarks>
    /// Reflect-101 不重复边缘：长度 3 的序列按 <c>...2,1|0,1,2|1,0...</c> 延伸，
    /// 周期为 <c>2n-2</c>。这与重复边缘的 symmetric/reflect 模式不同；长度 1 没有可反射邻居，固定映射 0。
    /// Wrap 使用非负模，能够处理核远大于图片时的多周期越界。
    /// </remarks>
    public static BorderSample Map(int index, int length, BorderMode mode)
    {
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
        if ((uint)index < (uint)length) return new BorderSample(index, false);
        return mode switch
        {
            BorderMode.Constant => new BorderSample(0, true),
            BorderMode.Replicate => new BorderSample(Math.Clamp(index, 0, length - 1), false),
            BorderMode.Reflect101 => new BorderSample(Reflect101(index, length), false),
            BorderMode.Wrap => new BorderSample(PositiveModulo(index, length), false),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "未知边界模式。")
        };
    }

    private static int Reflect101(int index, int length)
    {
        if (length == 1) return 0;
        var period = checked((length * 2) - 2);
        var folded = PositiveModulo(index, period);
        return folded < length ? folded : period - folded;
    }
    private static int PositiveModulo(int value, int modulus) => ((value % modulus) + modulus) % modulus;
}

/// <summary>规范的单平面真二维离散卷积执行器。</summary>
/// <remarks>
/// 行优先累加 <c>h(ky,kx)*f(x-kx,y-ky)</c>，没有相关运算常见的加号。输入通过只读 Span 观察，
/// raw double 与字节结果均新分配；取消或异常时调用方拿不到半成品。这里刻意不做不确定并行归约，确保像素探针
/// 能按相同顺序复算。可分离优化不是 V1 正确性的前提，所有预设都可安全走这条通用路径。
/// </remarks>
internal sealed class SpatialConvolver
{
    public ConvolutionPlaneResult Convolve(ReadOnlySpan<double> source, int width, int height,
        ConvolutionKernel kernel, BorderDefinition border, KernelNormalizationDefinition normalization,
        double bias, CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(bias)) throw new ArgumentOutOfRangeException(nameof(bias), "偏置必须有限。");
        var raw = ConvolveRaw(source, width, height, kernel, border, normalization, cancellationToken);
        return Quantize(raw.ValueSpan, width, height, raw.Divisor, bias, cancellationToken);
    }

    /// <summary>只执行卷积数学核心并返回未经偏置、舍入或 byte 裁切的结果。</summary>
    /// <remarks>
    /// 频域滤波的空间近似必须在 raw double 层比较；如果先量化，两条路径的负值和过冲都会被裁掉，
    /// 误差会被人为缩小。原有 <see cref="Convolve"/> 继续调用本方法后量化，因此既有产品行为不变，
    /// 也没有复制第二套卷积循环。
    /// </remarks>
    public RawConvolutionResult ConvolveRaw(ReadOnlySpan<double> source, int width, int height,
        ConvolutionKernel kernel, BorderDefinition border, KernelNormalizationDefinition normalization,
        CancellationToken cancellationToken = default)
    {
        if (width <= 0 || height <= 0 || source.Length != checked(width * height))
            throw new ArgumentException("输入平面长度与宽高不一致。", nameof(source));
        ArgumentNullException.ThrowIfNull(kernel); ArgumentNullException.ThrowIfNull(border);
        border.Validate();
        var divisor = ConvolutionNormalizer.ResolveDivisor(kernel, normalization);
        var raw = new double[source.Length];
        var coefficients = kernel.CoefficientSpan;
        for (var y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < width; x++)
            {
                double accumulator = 0;
                for (var row = 0; row < kernel.Size; row++)
                {
                    var ky = row - kernel.Radius;
                    var sampleY = BorderIndexMapper.Map(y - ky, height, border.Mode);
                    for (var column = 0; column < kernel.Size; column++)
                    {
                        var coefficient = coefficients[(row * kernel.Size) + column];
                        if (coefficient == 0) continue;
                        var kx = column - kernel.Radius;
                        var sampleX = BorderIndexMapper.Map(x - kx, width, border.Mode);
                        var sample = sampleX.IsConstant || sampleY.IsConstant
                            ? border.ConstantValue
                            : source[(sampleY.Index * width) + sampleX.Index];
                        accumulator += coefficient * sample;
                    }
                }
                raw[(y * width) + x] = accumulator / divisor;
            }
        }
        return new RawConvolutionResult(width, height, raw, divisor);
    }

    internal static ConvolutionPlaneResult Quantize(ReadOnlySpan<double> raw, int width, int height,
        double divisor, double bias, CancellationToken cancellationToken = default)
    {
        var bytes = new byte[raw.Length];
        var rawMin = double.PositiveInfinity; var rawMax = double.NegativeInfinity;
        var biasedMin = double.PositiveInfinity; var biasedMax = double.NegativeInfinity;
        long low = 0, high = 0;
        for (var index = 0; index < raw.Length; index++)
        {
            if ((index & 16383) == 0) cancellationToken.ThrowIfCancellationRequested();
            var rawValue = raw[index]; var value = rawValue + bias;
            rawMin = Math.Min(rawMin, rawValue); rawMax = Math.Max(rawMax, rawValue);
            biasedMin = Math.Min(biasedMin, value); biasedMax = Math.Max(biasedMax, value);
            var rounded = Math.Round(value, MidpointRounding.AwayFromZero);
            if (rounded < 0) low++; else if (rounded > 255) high++;
            bytes[index] = (byte)Math.Clamp(rounded, 0, 255);
        }
        var stats = raw.Length == 0 ? ConvolutionStatistics.Empty : new(rawMin, rawMax, biasedMin, biasedMax, low, high);
        return new ConvolutionPlaneResult(width, height, raw, bytes, divisor, stats);
    }
}

/// <summary>空间卷积数学核心的不可变结果；构造时复制缓冲，调用方不能修改比较证据。</summary>
internal sealed class RawConvolutionResult
{
    private readonly double[] _values;

    public RawConvolutionResult(int width, int height, ReadOnlySpan<double> values, double divisor)
    {
        if (width <= 0 || height <= 0 || values.Length != checked(width * height))
            throw new ArgumentException("raw 卷积缓冲长度与宽高不一致。", nameof(values));
        Width = width;
        Height = height;
        Divisor = divisor;
        _values = values.ToArray();
    }

    public int Width { get; }
    public int Height { get; }
    public double Divisor { get; }
    public ReadOnlyMemory<double> Values => new((double[])_values.Clone());
    internal ReadOnlySpan<double> ValueSpan => _values;
}
