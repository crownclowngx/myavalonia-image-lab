using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Domain.Frequency;

/// <summary>把每个 8×8 块的 DCT 对数幅度投影为只读灰度图，供界面解释频域分布。</summary>
internal sealed class FrequencySpectrumProjector(Dct8x8Transform transform)
{
    public PixelImage Create(PixelImage image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        var luma = ColorSpaceConverter.ExtractLuma(image);
        var magnitudes = new double[checked((int)image.Size.PixelCount)];
        double maximum = 0d;
        Span<double> spatial = stackalloc double[64];
        Span<double> frequency = stackalloc double[64];
        var blockColumns = image.Size.Width / 8;
        var blockRows = image.Size.Height / 8;
        for (var blockY = 0; blockY < blockRows; blockY++)
        {
            for (var blockX = 0; blockX < blockColumns; blockX++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (var y = 0; y < 8; y++)
                {
                    for (var x = 0; x < 8; x++)
                    {
                        spatial[(y * 8) + x] = luma[(blockX * 8) + x, (blockY * 8) + y] - 128d;
                    }
                }

                transform.Forward(spatial, frequency);
                for (var v = 0; v < 8; v++)
                {
                    for (var u = 0; u < 8; u++)
                    {
                        var value = Math.Log(1d + Math.Abs(frequency[(v * 8) + u]));
                        var target = (((blockY * 8) + v) * image.Size.Width) + (blockX * 8) + u;
                        magnitudes[target] = value;
                        maximum = Math.Max(maximum, value);
                    }
                }
            }
        }

        var rgba = new byte[checked((int)(image.Size.PixelCount * 4))];
        for (var i = 0; i < magnitudes.Length; i++)
        {
            var level = maximum == 0d ? (byte)0 : (byte)Math.Clamp((int)Math.Round(magnitudes[i] / maximum * 255d), 0, 255);
            var offset = i * 4;
            rgba[offset] = level;
            rgba[offset + 1] = level;
            rgba[offset + 2] = level;
            rgba[offset + 3] = 255;
        }

        return new PixelImage(image.Size, rgba);
    }
}
