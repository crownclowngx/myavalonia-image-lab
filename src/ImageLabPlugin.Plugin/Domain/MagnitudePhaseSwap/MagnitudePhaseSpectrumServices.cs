using System.Numerics;
using ImageLabPlugin.Domain.Shared.Spectral;
using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Domain.MagnitudePhaseSwap;

/// <summary>只把规范画布转换成共享 FFT 频谱，不认识任何产品实验模式。</summary>
internal sealed class MagnitudePhaseSpectrumBuilder(Fft2DTransform fft)
{
    public FrequencySpectrum Build(FrequencyPairCanvas canvas, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var values = new Complex[checked(canvas.Size * canvas.Size)];
        var source = canvas.Values.Span;
        for (var i = 0; i < values.Length; i++)
        {
            if ((i & 16383) == 0) cancellationToken.ThrowIfCancellationRequested();
            values[i] = new Complex(source[i], 0d);
        }
        fft.Forward(values, canvas.Size, canvas.Size, cancellationToken);
        return new FrequencySpectrum(new ImageSize(canvas.Size, canvas.Size), canvas.Size, canvas.Size, values);
    }
}

/// <summary>为 A/B/Result 建立统一幅度量程，并把相位无定义点绘成固定纹理。</summary>
/// <remarks>结果工作频谱在 IFFT 前直接投影，不复制第二份 Complex 数组，从而保持“一份工作副本”的峰值预算。</remarks>
internal sealed class MagnitudePhaseSpectrumProjector(SpectrumProjector projector)
{
    public SpectrumDisplayScale CreateSourceScale(FrequencySpectrum a, FrequencySpectrum b) =>
        projector.CreateSharedScale(a, b, SpectrumMagnitudeMode.Logarithmic);

    public SpectrumDisplayScale ExtendScale(FrequencySpectrum shape, Complex[] result, SpectrumDisplayScale sourceScale)
    {
        var resultScale = projector.CreateSharedScale(shape, result, SpectrumMagnitudeMode.Logarithmic);
        return new SpectrumDisplayScale(SpectrumMagnitudeMode.Logarithmic,
            Math.Max(sourceScale.MagnitudeLimit, resultScale.MagnitudeLimit));
    }

    public PixelImage Magnitude(FrequencySpectrum spectrum, SpectrumDisplayScale scale,
        CancellationToken cancellationToken = default) => projector.CreateMagnitude(spectrum, scale, cancellationToken);

    public PixelImage Magnitude(FrequencySpectrum shape, Complex[] values, SpectrumDisplayScale scale,
        CancellationToken cancellationToken = default) => projector.CreateMagnitude(shape, values, scale, cancellationToken);

    public PixelImage Phase(FrequencySpectrum spectrum, CancellationToken cancellationToken = default) =>
        ProjectPhase(spectrum.Values.Span, spectrum.PaddedWidth, cancellationToken);

    public PixelImage Phase(FrequencySpectrum shape, Complex[] values, CancellationToken cancellationToken = default)
    {
        if (values.Length != shape.ValueCount) throw new ArgumentException("结果频谱与形状不一致。", nameof(values));
        return ProjectPhase(values, shape.PaddedWidth, cancellationToken);
    }

    private static PixelImage ProjectPhase(ReadOnlySpan<Complex> values, int size, CancellationToken cancellationToken)
    {
        double maximum = 0d;
        foreach (var value in values) maximum = Math.Max(maximum, value.Magnitude);
        var threshold = Math.Max(1e-12, maximum * 1e-12);
        var rgba = new byte[checked(values.Length * 4)];
        for (var displayY = 0; displayY < size; displayY++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var displayX = 0; displayX < size; displayX++)
            {
                var internalX = (displayX + (size / 2)) % size;
                var internalY = (displayY + (size / 2)) % size;
                var value = values[(internalY * size) + internalX];
                var offset = ((displayY * size) + displayX) * 4;
                if (value.Magnitude <= threshold)
                {
                    // 棋盘纹理同时提供亮度和形状线索，不依赖单一颜色表达“相位无数据”。
                    var level = ((displayX / 6) + (displayY / 6)) % 2 == 0 ? (byte)24 : (byte)48;
                    rgba[offset] = rgba[offset + 1] = rgba[offset + 2] = level;
                }
                else
                {
                    var hue = (value.Phase + Math.PI) / (2d * Math.PI);
                    var rgb = Hsv(hue, .82d, .95d);
                    rgba[offset] = rgb.R; rgba[offset + 1] = rgb.G; rgba[offset + 2] = rgb.B;
                }
                rgba[offset + 3] = 255;
            }
        }
        return new PixelImage(new ImageSize(size, size), rgba);
    }

    private static (byte R, byte G, byte B) Hsv(double hue, double saturation, double value)
    {
        var h = ((hue % 1d) + 1d) % 1d * 6d;
        var sector = (int)Math.Floor(h); var fraction = h - sector;
        var p = value * (1d - saturation); var q = value * (1d - (saturation * fraction));
        var t = value * (1d - (saturation * (1d - fraction)));
        var (r, g, b) = sector switch
        {
            0 => (value, t, p), 1 => (q, value, p), 2 => (p, value, t),
            3 => (p, q, value), 4 => (t, p, value), _ => (value, p, q)
        };
        return ((byte)Math.Round(r * 255d), (byte)Math.Round(g * 255d), (byte)Math.Round(b * 255d));
    }
}
