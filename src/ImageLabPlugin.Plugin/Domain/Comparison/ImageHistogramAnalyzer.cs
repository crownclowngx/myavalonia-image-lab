using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Domain.Comparison;

/// <summary>一张图片的 R/G/B/Y/Cb/Cr 六通道 256-bin 原始计数。</summary>
internal sealed class ImageHistogram
{
    private readonly long[][] _bins;

    public ImageHistogram(long[][] bins)
    {
        if (bins.Length != 6 || bins.Any(static values => values.Length != 256))
            throw new ArgumentException("直方图必须包含六个 256-bin 通道。", nameof(bins));
        _bins = bins.Select(static values => (long[])values.Clone()).ToArray();
    }

    public IReadOnlyList<long> GetBins(ImageChannel channel) => _bins[(int)channel];
}

internal sealed record ImagePairHistograms(ImageHistogram Reference, ImageHistogram Candidate);

/// <summary>独立累计双图六通道直方图；统计仍基于完整图片而不是显示代理。</summary>
internal sealed class ImageHistogramAnalyzer(ImagePairValidator validator)
{
    public ImagePairHistograms Analyze(PixelImage reference, PixelImage candidate, CancellationToken cancellationToken = default)
    {
        validator.EnsureComparable(reference, candidate);
        return new ImagePairHistograms(AnalyzeOne(reference, cancellationToken), AnalyzeOne(candidate, cancellationToken));
    }

    private static ImageHistogram AnalyzeOne(PixelImage image, CancellationToken cancellationToken)
    {
        var bins = Enumerable.Range(0, 6).Select(static _ => new long[256]).ToArray();
        var pixels = image.Rgba.Span;
        for (var y = 0; y < image.Size.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < image.Size.Width; x++)
            {
                var offset = ((y * image.Size.Width) + x) * 4;
                var red = pixels[offset]; var green = pixels[offset + 1]; var blue = pixels[offset + 2];
                bins[(int)ImageChannel.Red][red]++;
                bins[(int)ImageChannel.Green][green]++;
                bins[(int)ImageChannel.Blue][blue]++;
                bins[(int)ImageChannel.Luma][ToBin(ColorSpaceConverter.ToLuma(red, green, blue))]++;
                bins[(int)ImageChannel.ChromaBlue][ToBin(128d - (0.168736d * red) - (0.331264d * green) + (0.5d * blue))]++;
                bins[(int)ImageChannel.ChromaRed][ToBin(128d + (0.5d * red) - (0.418688d * green) - (0.081312d * blue))]++;
            }
        }
        return new ImageHistogram(bins);
    }

    private static int ToBin(double value) => Math.Clamp((int)Math.Round(value), 0, 255);
}
