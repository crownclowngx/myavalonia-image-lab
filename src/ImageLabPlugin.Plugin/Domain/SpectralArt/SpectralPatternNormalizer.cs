using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Domain.SpectralArt;

/// <summary>把已解码 RGBA 图片规范化为有界灰度或二值 Pattern。</summary>
/// <remarks>
/// Alpha 为零的像素固定映射为零权重；其他像素先与用户选择的黑/白背景合成，再把“越暗”解释为越强前景。
/// 灰度模式复用项目面积采样，二值模式使用专用最近邻，保证二维码模块不会产生灰边。该服务不知道文件、字体、
/// FFT 和区域，因此 Logo、二维码以及文字栅格结果都走同一条确定性规范化路径。
/// </remarks>
internal sealed class SpectralPatternNormalizer(ImageAreaResampler areaResampler)
{
    public SpectralPattern Normalize(
        PixelImage source,
        SpectralPatternNormalizationOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        var sourceWeights = CreateSourceWeights(source, options, cancellationToken);
        var targetSize = new ImageSize(options.TargetWidth, options.TargetHeight);
        var normalized = options.SamplingMode == SpectralPatternSamplingMode.BinaryNearest
            ? ResizeNearest(sourceWeights, source.Size, targetSize, cancellationToken)
            : ResizeArea(sourceWeights, source.Size, targetSize, cancellationToken);
        return new SpectralPattern(targetSize.Width, targetSize.Height, normalized,
            options.SamplingMode, options.SourceKind);
    }

    private static double[] CreateSourceWeights(
        PixelImage source,
        SpectralPatternNormalizationOptions options,
        CancellationToken cancellationToken)
    {
        var result = new double[checked((int)source.Size.PixelCount)];
        var background = options.Background == SpectralPatternBackground.White ? 255d : 0d;
        for (var y = 0; y < source.Size.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < source.Size.Width; x++)
            {
                var pixel = source.GetPixel(x, y);
                if (pixel.A == 0)
                {
                    result[(y * source.Size.Width) + x] = 0d;
                    continue;
                }
                var alpha = pixel.A / 255d;
                var luma = ColorSpaceConverter.ToLuma(pixel.R, pixel.G, pixel.B);
                var composited = (alpha * luma) + ((1d - alpha) * background);
                var weight = 1d - (composited / 255d);
                if (options.Invert) weight = 1d - weight;
                result[(y * source.Size.Width) + x] = options.SamplingMode == SpectralPatternSamplingMode.BinaryNearest
                    ? weight >= options.BinaryThreshold ? 1d : 0d
                    : Math.Clamp(weight, 0d, 1d);
            }
        }
        return result;
    }

    private PixelImage ToWeightImage(double[] weights, ImageSize size)
    {
        var rgba = new byte[checked(weights.Length * 4)];
        for (var i = 0; i < weights.Length; i++)
        {
            var level = (byte)Math.Clamp((int)Math.Round(weights[i] * 255d,
                MidpointRounding.ToEven), 0, 255);
            var offset = i * 4;
            rgba[offset] = rgba[offset + 1] = rgba[offset + 2] = level;
            rgba[offset + 3] = 255;
        }
        return new PixelImage(size, rgba);
    }

    private double[] ResizeArea(
        double[] weights,
        ImageSize sourceSize,
        ImageSize targetSize,
        CancellationToken cancellationToken)
    {
        var resized = areaResampler.Resize(ToWeightImage(weights, sourceSize), targetSize, cancellationToken);
        var result = new double[checked((int)targetSize.PixelCount)];
        for (var y = 0; y < targetSize.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < targetSize.Width; x++)
                result[(y * targetSize.Width) + x] = resized.GetPixel(x, y).R / 255d;
        }
        return result;
    }

    private static double[] ResizeNearest(
        double[] weights,
        ImageSize sourceSize,
        ImageSize targetSize,
        CancellationToken cancellationToken)
    {
        var result = new double[checked((int)targetSize.PixelCount)];
        for (var y = 0; y < targetSize.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceY = Math.Min(sourceSize.Height - 1,
                (int)Math.Floor((y + 0.5d) * sourceSize.Height / targetSize.Height));
            for (var x = 0; x < targetSize.Width; x++)
            {
                var sourceX = Math.Min(sourceSize.Width - 1,
                    (int)Math.Floor((x + 0.5d) * sourceSize.Width / targetSize.Width));
                result[(y * targetSize.Width) + x] = weights[(sourceY * sourceSize.Width) + sourceX];
            }
        }
        return result;
    }
}
