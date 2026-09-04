using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Domain.Shared.Perturbations;

namespace ImageLabPlugin.Domain.Shared.ArtEffects;

internal enum ImageArtEffectKind
{
    GaussianBlur,
    Bloom,
    Grain,
}

internal sealed record BlurEffectSettings(bool Enabled, double Sigma);

internal sealed record BloomEffectSettings(
    bool Enabled,
    double Threshold,
    double Sigma,
    double Strength);

internal sealed record GrainEffectSettings(bool Enabled, double Amount, long Seed);

/// <summary>冻结一次艺术导出的全部效果参数；验证集中在领域入口，所有调用方得到同一语义。</summary>
internal sealed record ImageArtEffectOptions(
    BlurEffectSettings Blur,
    BloomEffectSettings Bloom,
    GrainEffectSettings Grain)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Blur);
        ArgumentNullException.ThrowIfNull(Bloom);
        ArgumentNullException.ThrowIfNull(Grain);
        RequireFiniteRange(Blur.Sigma, 0d, 10d, nameof(Blur.Sigma));
        RequireFiniteRange(Bloom.Threshold, 0d, 1d, nameof(Bloom.Threshold));
        RequireFiniteRange(Bloom.Sigma, 0.1d, 10d, nameof(Bloom.Sigma));
        RequireFiniteRange(Bloom.Strength, 0d, 4d, nameof(Bloom.Strength));
        RequireFiniteRange(Grain.Amount, 0d, 100d, nameof(Grain.Amount));
    }

    private static void RequireFiniteRange(double value, double minimum, double maximum, string name)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(name, value, $"参数必须是 {minimum}–{maximum} 的有限数值。");
        }
    }
}

/// <summary>单一艺术效果策略；策略只处理 RGBA 像素，不知道 JSON、文件或 Workflow。</summary>
internal interface IImageArtEffectProcessor
{
    ImageArtEffectKind Kind { get; }
    PixelImage Apply(PixelImage source, ImageArtEffectOptions options, CancellationToken cancellationToken);
}

internal sealed class GaussianBlurArtEffectProcessor : IImageArtEffectProcessor
{
    public ImageArtEffectKind Kind => ImageArtEffectKind.GaussianBlur;

    public PixelImage Apply(
        PixelImage source,
        ImageArtEffectOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        return !options.Blur.Enabled || options.Blur.Sigma == 0d
            ? source.Clone()
            : GaussianBlur.Apply(source, options.Blur.Sigma, cancellationToken);
    }
}

internal sealed class BloomArtEffectProcessor : IImageArtEffectProcessor
{
    public ImageArtEffectKind Kind => ImageArtEffectKind.Bloom;

    public PixelImage Apply(
        PixelImage source,
        ImageArtEffectOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        var settings = options.Bloom;
        if (!settings.Enabled || settings.Strength == 0d)
        {
            return source.Clone();
        }

        var sourceBytes = source.Rgba.Span;
        var highlightBytes = new byte[sourceBytes.Length];
        var threshold = settings.Threshold * 255d;
        for (var y = 0; y < source.Size.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < source.Size.Width; x++)
            {
                var offset = PerturbationPixels.Offset(source.Size, x, y);
                // Rec.709 权重冻结高光判定；低于阈值的像素必须为纯黑，避免暗部被光晕抬升。
                var luminance = (0.2126d * sourceBytes[offset]) +
                                (0.7152d * sourceBytes[offset + 1]) +
                                (0.0722d * sourceBytes[offset + 2]);
                if (luminance >= threshold)
                {
                    highlightBytes[offset] = sourceBytes[offset];
                    highlightBytes[offset + 1] = sourceBytes[offset + 1];
                    highlightBytes[offset + 2] = sourceBytes[offset + 2];
                }
                highlightBytes[offset + 3] = sourceBytes[offset + 3];
            }
        }

        var blurredHighlight = GaussianBlur.Apply(
            new PixelImage(source.Size, highlightBytes), settings.Sigma, cancellationToken);
        var bloom = blurredHighlight.Rgba.Span;
        var output = source.Clone();
        var target = output.WritableRgba;
        for (var y = 0; y < source.Size.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rowEnd = checked((y + 1) * source.Size.Width * 4);
            for (var offset = checked(y * source.Size.Width * 4); offset < rowEnd; offset += 4)
            {
                for (var channel = 0; channel < 3; channel++)
                {
                    target[offset + channel] = PerturbationPixels.ClampRound(
                        sourceBytes[offset + channel] + (settings.Strength * bloom[offset + channel]));
                }
            }
        }
        return output;
    }
}

internal sealed class GrainArtEffectProcessor : IImageArtEffectProcessor
{
    public ImageArtEffectKind Kind => ImageArtEffectKind.Grain;

    public PixelImage Apply(
        PixelImage source,
        ImageArtEffectOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        var settings = options.Grain;
        var output = source.Clone();
        if (!settings.Enabled || settings.Amount == 0d)
        {
            return output;
        }

        // long 到 ulong 使用补码位模式，确保负 Seed 也有跨运行稳定的 SplitMix64 序列。
        var random = new PerturbationRandom(unchecked((ulong)settings.Seed));
        var bytes = output.WritableRgba;
        var hasSpare = false;
        var spare = 0d;
        for (var y = 0; y < source.Size.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < source.Size.Width; x++)
            {
                var offset = PerturbationPixels.Offset(source.Size, x, y);
                for (var channel = 0; channel < 3; channel++)
                {
                    double normal;
                    if (hasSpare)
                    {
                        normal = spare;
                        hasSpare = false;
                    }
                    else
                    {
                        // Box–Muller 一次产生两个标准正态样本，只保留一个备用值，不分配噪声图。
                        var u1 = Math.Max(double.Epsilon, random.NextDouble());
                        var u2 = random.NextDouble();
                        var radius = Math.Sqrt(-2d * Math.Log(u1));
                        normal = radius * Math.Cos(2d * Math.PI * u2);
                        spare = radius * Math.Sin(2d * Math.PI * u2);
                        hasSpare = true;
                    }
                    bytes[offset + channel] = PerturbationPixels.ClampRound(
                        bytes[offset + channel] + (normal * settings.Amount));
                }
            }
        }
        return output;
    }
}

/// <summary>用显式构造顺序冻结 G0007 的 Blur→Bloom→Grain 流水线。</summary>
internal sealed class ImageArtEffectPipeline(
    GaussianBlurArtEffectProcessor blur,
    BloomArtEffectProcessor bloom,
    GrainArtEffectProcessor grain)
{
    private readonly IImageArtEffectProcessor[] _processors =
    [
        blur ?? throw new ArgumentNullException(nameof(blur)),
        bloom ?? throw new ArgumentNullException(nameof(bloom)),
        grain ?? throw new ArgumentNullException(nameof(grain)),
    ];

    public PixelImage Apply(
        PixelImage source,
        ImageArtEffectOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        var current = source.Clone();
        foreach (var processor in _processors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            current = processor.Apply(current, options, cancellationToken);
        }
        return current;
    }
}
