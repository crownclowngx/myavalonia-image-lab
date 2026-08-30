using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Domain.Frequency;

/// <summary>把分析代理的逐块 DCT 对数幅度投影成统一归一化灰度图。</summary>
internal sealed class DctSpectrumProjector(ImageChannelConverter channelConverter, Dct8x8Transform transform)
{
    public PixelImage Create(PixelImage image, ImageChannel channel, CancellationToken cancellationToken = default)
    {
        var plane = channelConverter.Extract(image, channel, cancellationToken);
        var magnitudes = new double[checked((int)image.Size.PixelCount)];
        Array.Fill(magnitudes, -1d); // 非完整边缘用棋盘中性色，而不是伪造补零 DCT。
        Span<double> spatial = stackalloc double[64];
        Span<double> frequency = stackalloc double[64];
        double maximum = 0d;
        for (var blockY = 0; blockY < image.Size.Height / 8; blockY++)
        for (var blockX = 0; blockX < image.Size.Width / 8; blockX++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var y = 0; y < 8; y++)
            for (var x = 0; x < 8; x++) spatial[(y * 8) + x] = plane[(blockX * 8) + x, (blockY * 8) + y];
            // Dct8x8Transform 内部统一执行 -128；调用层传入原始通道值，防止双重中心化。
            transform.Forward(spatial, frequency);
            for (var v = 0; v < 8; v++)
            for (var u = 0; u < 8; u++)
            {
                var value = Math.Log(1d + Math.Abs(frequency[(v * 8) + u]));
                magnitudes[(((blockY * 8) + v) * image.Size.Width) + (blockX * 8) + u] = value;
                maximum = Math.Max(maximum, value);
            }
        }

        var rgba = new byte[checked((int)(image.Size.PixelCount * 4))];
        for (var y = 0; y < image.Size.Height; y++)
        for (var x = 0; x < image.Size.Width; x++)
        {
            var index = (y * image.Size.Width) + x;
            var level = magnitudes[index] < 0d ? (byte)(((x / 4 + y / 4) & 1) == 0 ? 52 : 76) :
                maximum == 0d ? (byte)0 : (byte)Math.Clamp((int)Math.Round(magnitudes[index] / maximum * 255d), 0, 255);
            var offset = index * 4; rgba[offset] = rgba[offset + 1] = rgba[offset + 2] = level; rgba[offset + 3] = 255;
        }
        return new PixelImage(image.Size, rgba);
    }
}
