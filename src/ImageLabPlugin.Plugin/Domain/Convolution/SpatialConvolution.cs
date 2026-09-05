using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Domain.Shared.Spatial;

namespace ImageLabPlugin.Domain.Convolution;

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
        ConvolutionChannelMode.Red => ImageChannel.Red,
        ConvolutionChannelMode.Green => ImageChannel.Green,
        ConvolutionChannelMode.Blue => ImageChannel.Blue,
        ConvolutionChannelMode.Luma => ImageChannel.Luma,
        ConvolutionChannelMode.ChromaBlue => ImageChannel.ChromaBlue,
        ConvolutionChannelMode.ChromaRed => ImageChannel.ChromaRed,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "RGB 模式应由三通道路径处理。")
    };
}
