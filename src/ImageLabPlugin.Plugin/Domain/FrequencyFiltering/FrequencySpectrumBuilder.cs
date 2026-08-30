using System.Numerics;
using ImageLabPlugin.Domain.Frequency;
using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Domain.FrequencyFiltering;

/// <summary>把已抽取通道建立为补零频谱，并集中定义补零区域的中性值。</summary>
/// <remarks>
/// 本类型只处理数值缓冲，不解码文件也不拥有 Session。准备代理与显式原尺寸执行共享它，避免两条路径在
/// Cb/Cr 的 128 中性填充或 2048² 预算上产生分叉。
/// </remarks>
internal sealed class FrequencySpectrumBuilder(Fft2DTransform fft)
{
    public FrequencySpectrum Build(ImageChannelPlane plane, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plane);
        var paddedWidth = FrequencySpectrum.NextPowerOfTwo(plane.Size.Width);
        var paddedHeight = FrequencySpectrum.NextPowerOfTwo(plane.Size.Height);
        var count = checked(paddedWidth * paddedHeight);
        if (count > FrequencySpectrum.MaximumComplexValues) throw new InvalidOperationException("FFT 补零样本超过 2048×2048 预算。");
        var values = new Complex[count];
        var neutral = ImageChannelConverter.NeutralValue(plane.Channel);
        if (neutral != 0d) Array.Fill(values, new Complex(neutral, 0d));
        var source = plane.Values.Span;
        for (var y = 0; y < plane.Size.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < plane.Size.Width; x++) values[(y * paddedWidth) + x] = new Complex(source[(y * plane.Size.Width) + x], 0d);
        }
        fft.Forward(values, paddedWidth, paddedHeight, cancellationToken);
        return new FrequencySpectrum(plane.Size, paddedWidth, paddedHeight, values);
    }

    public double[] CreatePaddedSpatialPlane(ImageChannelPlane plane, FrequencySpectrum spectrum)
    {
        if (plane.Size != spectrum.SourceSize) throw new ArgumentException("通道与频谱源尺寸不一致。", nameof(plane));
        var values = new double[checked(spectrum.PaddedWidth * spectrum.PaddedHeight)];
        var neutral = ImageChannelConverter.NeutralValue(plane.Channel);
        if (neutral != 0d) Array.Fill(values, neutral);
        for (var y = 0; y < plane.Size.Height; y++)
            plane.Values.Span.Slice(y * plane.Size.Width, plane.Size.Width).CopyTo(values.AsSpan(y * spectrum.PaddedWidth, plane.Size.Width));
        return values;
    }
}
