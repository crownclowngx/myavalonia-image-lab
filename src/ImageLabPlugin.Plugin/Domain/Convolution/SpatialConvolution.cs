using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Domain.Convolution;

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

/// <summary>只负责将两个线性梯度平面组合成非线性 Magnitude。</summary>
internal sealed class GradientCombiner
{
    public ConvolutionPlaneResult Combine(ConvolutionPlaneResult x, ConvolutionPlaneResult y, double bias,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(x); ArgumentNullException.ThrowIfNull(y);
        if (x.Width != y.Width || x.Height != y.Height) throw new ArgumentException("X/Y 梯度尺寸必须一致。");
        var xValues = x.RawValues.Span; var yValues = y.RawValues.Span; var magnitude = new double[xValues.Length];
        for (var index = 0; index < magnitude.Length; index++)
        {
            if ((index & 16383) == 0) cancellationToken.ThrowIfCancellationRequested();
            magnitude[index] = Math.Sqrt((xValues[index] * xValues[index]) + (yValues[index] * yValues[index]));
        }
        // divisor 已经分别应用到 Gx/Gy。Magnitude 形成后只加一次偏置，不能对两个分量分别偏置。
        return SpatialConvolver.Quantize(magnitude, x.Width, x.Height, 1d, bias, cancellationToken);
    }
}

internal sealed record ConvolutionChannelResult(string ChannelName, ConvolutionPlaneResult Plane);
internal sealed record ConvolutionImageResult(
    PixelImage Image,
    IReadOnlyList<ConvolutionChannelResult> Channels,
    int ColorReconstructionClippedPixels,
    string RecipeFingerprint);

/// <summary>协调通道抽取、空间卷积与 RGBA 重建，不生成预设也不拥有 Session。</summary>
/// <remarks>
/// Alpha 永远从源图逐字节复制，因为 V1 处理未预乘 RGBA，卷积 Alpha 会改变透明度且制造颜色边缘。
/// Y/Cb/Cr 只替换选中分量，再由既有颜色转换器回写；由于 RGB 色域是有限立方体，合法的分量组合仍可能回写裁切，
/// 因而裁切像素数独立报告。
/// </remarks>
internal sealed class ConvolutionImageProcessor(
    ImageChannelConverter channelConverter,
    SpatialConvolver convolver,
    GradientCombiner gradientCombiner)
{
    public ConvolutionImageResult Process(PixelImage source, ConvolutionRecipe recipe, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(recipe); recipe.Validate();
        if (recipe.Channel == ConvolutionChannelMode.Rgb) return ProcessRgb(source, recipe, cancellationToken);
        var channel = ToImageChannel(recipe.Channel);
        var input = channelConverter.Extract(source, channel, cancellationToken);
        var result = ProcessPlane(input.Values.Span, source.Size.Width, source.Size.Height, recipe, cancellationToken);
        var reconstructedPlane = new ImageChannelPlane(source.Size, channel, result.Bytes.Span.ToArray().Select(static value => (double)value).ToArray());
        var reconstructed = channelConverter.Apply(source, reconstructedPlane);
        return new ConvolutionImageResult(reconstructed.Image, [new(channel.ToString(), result)], reconstructed.ClippedPixelCount, recipe.Fingerprint());
    }

    private ConvolutionImageResult ProcessRgb(PixelImage source, ConvolutionRecipe recipe, CancellationToken token)
    {
        var channels = new[] { ImageChannel.Red, ImageChannel.Green, ImageChannel.Blue };
        var results = new List<ConvolutionChannelResult>(3);
        foreach (var channel in channels)
        {
            token.ThrowIfCancellationRequested();
            var input = channelConverter.Extract(source, channel, token);
            results.Add(new ConvolutionChannelResult(channel.ToString(), ProcessPlane(input.Values.Span, source.Size.Width, source.Size.Height, recipe, token)));
        }
        var rgba = source.Rgba.ToArray();
        for (var index = 0; index < source.Size.PixelCount; index++)
        {
            if ((index & 16383) == 0) token.ThrowIfCancellationRequested();
            var pixel = checked((int)index); var offset = pixel * 4;
            rgba[offset] = results[0].Plane.Bytes.Span[pixel]; rgba[offset + 1] = results[1].Plane.Bytes.Span[pixel]; rgba[offset + 2] = results[2].Plane.Bytes.Span[pixel];
        }
        return new ConvolutionImageResult(new PixelImage(source.Size, rgba), results, 0, recipe.Fingerprint());
    }

    private ConvolutionPlaneResult ProcessPlane(ReadOnlySpan<double> input, int width, int height, ConvolutionRecipe recipe, CancellationToken token)
    {
        if (recipe.Operator.Kind == ConvolutionOperatorKind.Single)
            return convolver.Convolve(input, width, height, recipe.Operator.PrimaryKernel, recipe.Border, recipe.Normalization, recipe.Bias, token);
        var secondary = recipe.Operator.SecondaryKernel ?? throw new InvalidOperationException("梯度 Y 核缺失。");
        if (recipe.GradientOutput == GradientOutputMode.X)
            return convolver.Convolve(input, width, height, recipe.Operator.PrimaryKernel, recipe.Border, recipe.Normalization, recipe.Bias, token);
        if (recipe.GradientOutput == GradientOutputMode.Y)
            return convolver.Convolve(input, width, height, secondary, recipe.Border, recipe.Normalization, recipe.Bias, token);
        var x = convolver.Convolve(input, width, height, recipe.Operator.PrimaryKernel, recipe.Border, recipe.Normalization, 0, token);
        var y = convolver.Convolve(input, width, height, secondary, recipe.Border, recipe.Normalization, 0, token);
        return gradientCombiner.Combine(x, y, recipe.Bias, token);
    }

    private static ImageChannel ToImageChannel(ConvolutionChannelMode mode) => mode switch
    {
        ConvolutionChannelMode.Red => ImageChannel.Red, ConvolutionChannelMode.Green => ImageChannel.Green,
        ConvolutionChannelMode.Blue => ImageChannel.Blue, ConvolutionChannelMode.Luma => ImageChannel.Luma,
        ConvolutionChannelMode.ChromaBlue => ImageChannel.ChromaBlue, ConvolutionChannelMode.ChromaRed => ImageChannel.ChromaRed,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "RGB 模式应由三通道路径处理。")
    };
}
