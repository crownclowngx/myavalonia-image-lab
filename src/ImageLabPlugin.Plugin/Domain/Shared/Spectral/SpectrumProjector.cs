using System.Numerics;
using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Domain.Shared.Spectral;

internal enum SpectrumMagnitudeMode { Linear, Logarithmic, Percentile }
internal enum SpectrumViewMode { Magnitude, Phase, Dct }

/// <summary>显式冻结一组频谱预览共用的幅度量程，避免各自拉伸制造虚假的视觉差异。</summary>
internal readonly record struct SpectrumDisplayScale
{
    public SpectrumDisplayScale(SpectrumMagnitudeMode mode, double magnitudeLimit)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (!double.IsFinite(magnitudeLimit) || magnitudeLimit < 0d)
            throw new ArgumentOutOfRangeException(nameof(magnitudeLimit));
        Mode = mode;
        MagnitudeLimit = magnitudeLimit;
    }

    public SpectrumMagnitudeMode Mode { get; }
    public double MagnitudeLimit { get; }
}

internal sealed record FrequencyPointInfo(
    FrequencyPoint Coordinates,
    double Magnitude,
    double? PhaseRadians,
    double NormalizedEnergy,
    FrequencyRegion Region);

/// <summary>把只读复数频谱投影成中心化 RGBA 图片，并提供无副作用的频点查询。</summary>
internal sealed class SpectrumProjector
{
    public PixelImage CreateMagnitude(FrequencySpectrum spectrum, SpectrumMagnitudeMode mode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spectrum);
        return CreateMagnitude(spectrum, new SpectrumDisplayScale(mode,
            ResolveMagnitudeLimit(spectrum.Values.Span, mode)), cancellationToken);
    }

    /// <summary>为两张待比较频谱建立一个共同量程；旧的单图投影入口保持原有自动量程语义。</summary>
    public SpectrumDisplayScale CreateSharedScale(
        FrequencySpectrum first,
        FrequencySpectrum second,
        SpectrumMagnitudeMode mode)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        var firstLimit = ResolveMagnitudeLimit(first.Values.Span, mode);
        var secondLimit = ResolveMagnitudeLimit(second.Values.Span, mode);
        return new SpectrumDisplayScale(mode, Math.Max(firstLimit, secondLimit));
    }

    /// <summary>为三张以上同屏频谱冻结一个共同量程，避免逐对比较时遗漏后续更大的幅度。</summary>
    /// <remarks>
    /// 该入口只扫描只读幅度，不保存频谱或创建额外 Complex 副本。Hybrid Image 需要同时比较 A、B、
    /// 低频、高频和结果五项事实，因此由通用投影器集中一次确定量程，比在产品层复制量程算法更可靠。
    /// </remarks>
    public SpectrumDisplayScale CreateSharedScale(
        IReadOnlyList<FrequencySpectrum> spectra,
        SpectrumMagnitudeMode mode)
    {
        ArgumentNullException.ThrowIfNull(spectra);
        if (spectra.Count == 0) throw new ArgumentException("至少需要一张频谱。", nameof(spectra));
        double maximum = 0d;
        foreach (var spectrum in spectra)
        {
            ArgumentNullException.ThrowIfNull(spectrum);
            maximum = Math.Max(maximum, ResolveMagnitudeLimit(spectrum.Values.Span, mode));
        }
        return new SpectrumDisplayScale(mode, maximum);
    }

    /// <summary>为只读 Session 频谱与调用方拥有的工作数组建立共同量程，不复制第二份完整频谱。</summary>
    internal SpectrumDisplayScale CreateSharedScale(
        FrequencySpectrum first,
        Complex[] secondValues,
        SpectrumMagnitudeMode mode)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(secondValues);
        if (secondValues.Length != first.ValueCount)
            throw new ArgumentException("工作频谱长度与源频谱不一致。", nameof(secondValues));
        return new SpectrumDisplayScale(mode, Math.Max(
            ResolveMagnitudeLimit(first.Values.Span, mode),
            ResolveMagnitudeLimit(secondValues, mode)));
    }

    /// <summary>使用调用方冻结的量程投影频谱，使写入前后灰度具有可比较含义。</summary>
    public PixelImage CreateMagnitude(
        FrequencySpectrum spectrum,
        SpectrumDisplayScale scale,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spectrum);
        var mode = scale.Mode;
        var limit = scale.MagnitudeLimit;
        var logarithmicLimit = Math.Log(1d + limit);
        return Project(spectrum, (value, _) =>
        {
            var magnitude = value.Magnitude;
            var normalized = mode switch
            {
                SpectrumMagnitudeMode.Logarithmic => logarithmicLimit <= 0d ? 0d : Math.Log(1d + Math.Min(magnitude, limit)) / logarithmicLimit,
                _ => limit <= 0d ? 0d : Math.Min(magnitude, limit) / limit
            };
            var level = ToByte(normalized * 255d);
            return (level, level, level);
        }, cancellationToken);
    }

    /// <summary>直接投影调用方拥有的工作频谱；用于 IFFT 消费工作数组之前生成结果预览。</summary>
    internal PixelImage CreateMagnitude(
        FrequencySpectrum shape,
        Complex[] values,
        SpectrumDisplayScale scale,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length != shape.ValueCount)
            throw new ArgumentException("工作频谱长度与频谱形状不一致。", nameof(values));
        var rgba = new byte[checked(values.Length * 4)];
        var limit = scale.MagnitudeLimit;
        var logarithmicLimit = Math.Log(1d + limit);
        for (var displayY = 0; displayY < shape.PaddedHeight; displayY++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var displayX = 0; displayX < shape.PaddedWidth; displayX++)
            {
                var point = FrequencyCoordinates.FromDisplay(displayX, displayY,
                    shape.PaddedWidth, shape.PaddedHeight);
                var magnitude = values[(point.InternalY * shape.PaddedWidth) + point.InternalX].Magnitude;
                var normalized = scale.Mode switch
                {
                    SpectrumMagnitudeMode.Logarithmic => logarithmicLimit <= 0d
                        ? 0d
                        : Math.Log(1d + Math.Min(magnitude, limit)) / logarithmicLimit,
                    _ => limit <= 0d ? 0d : Math.Min(magnitude, limit) / limit
                };
                var level = ToByte(normalized * 255d);
                var offset = ((displayY * shape.PaddedWidth) + displayX) * 4;
                rgba[offset] = rgba[offset + 1] = rgba[offset + 2] = level;
                rgba[offset + 3] = 255;
            }
        }
        return new PixelImage(new ImageSize(shape.PaddedWidth, shape.PaddedHeight), rgba);
    }

    public PixelImage CreatePhase(FrequencySpectrum spectrum, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spectrum);
        double maxMagnitude = 0d;
        foreach (var value in spectrum.Values.Span) maxMagnitude = Math.Max(maxMagnitude, value.Magnitude);
        var logLimit = Math.Log(1d + maxMagnitude);
        return Project(spectrum, (value, _) =>
        {
            if (maxMagnitude <= 0d || value.Magnitude <= maxMagnitude * 1e-12) return ((byte)18, (byte)18, (byte)18);
            var hue = (value.Phase + Math.PI) / (2d * Math.PI);
            var brightness = logLimit <= 0d ? 0d : Math.Log(1d + value.Magnitude) / logLimit;
            return HsvToRgb(hue, 0.85d, brightness);
        }, cancellationToken);
    }

    public FrequencyPointInfo Inspect(FrequencySpectrum spectrum, int displayX, int displayY, FrequencyBandBoundaries boundaries)
    {
        ArgumentNullException.ThrowIfNull(spectrum);
        var coordinates = FrequencyCoordinates.FromDisplay(displayX, displayY, spectrum.PaddedWidth, spectrum.PaddedHeight);
        var value = spectrum[coordinates.InternalX, coordinates.InternalY];
        double totalEnergy = 0d;
        foreach (var item in spectrum.Values.Span) totalEnergy += item.Magnitude * item.Magnitude;
        double? phase = value.Magnitude <= (Math.Sqrt(totalEnergy) * 1e-12) ? null : value.Phase;
        return new FrequencyPointInfo(
            coordinates,
            value.Magnitude,
            phase,
            totalEnergy <= 0d ? 0d : value.Magnitude * value.Magnitude / totalEnergy,
            boundaries.Classify(coordinates.Radius, coordinates.Kx == 0 && coordinates.Ky == 0));
    }

    private static PixelImage Project(
        FrequencySpectrum spectrum,
        Func<Complex, FrequencyPoint, (byte R, byte G, byte B)> color,
        CancellationToken cancellationToken)
    {
        var rgba = new byte[checked(spectrum.ValueCount * 4)];
        for (var displayY = 0; displayY < spectrum.PaddedHeight; displayY++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var displayX = 0; displayX < spectrum.PaddedWidth; displayX++)
            {
                var point = FrequencyCoordinates.FromDisplay(displayX, displayY, spectrum.PaddedWidth, spectrum.PaddedHeight);
                var pixel = color(spectrum[point.InternalX, point.InternalY], point);
                var offset = ((displayY * spectrum.PaddedWidth) + displayX) * 4;
                rgba[offset] = pixel.R;
                rgba[offset + 1] = pixel.G;
                rgba[offset + 2] = pixel.B;
                rgba[offset + 3] = 255;
            }
        }

        return new PixelImage(new ImageSize(spectrum.PaddedWidth, spectrum.PaddedHeight), rgba);
    }

    private static double ResolveMagnitudeLimit(ReadOnlySpan<Complex> values, SpectrumMagnitudeMode mode)
    {
        if (mode != SpectrumMagnitudeMode.Percentile)
        {
            double maximum = 0d;
            foreach (var value in values)
            {
                var magnitude = value.Magnitude;
                if (double.IsFinite(magnitude)) maximum = Math.Max(maximum, magnitude);
            }
            return maximum;
        }

        var magnitudes = new double[values.Length];
        var count = 0;
        for (var i = 0; i < values.Length; i++)
        {
            var magnitude = values[i].Magnitude;
            if (double.IsFinite(magnitude)) magnitudes[count++] = magnitude;
        }

        if (count == 0) return 0d;
        Array.Sort(magnitudes, 0, count);
        var index = Math.Clamp((int)Math.Ceiling((count * 0.995d)) - 1, 0, count - 1);
        return magnitudes[index];
    }

    private static byte ToByte(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);

    private static (byte R, byte G, byte B) HsvToRgb(double hue, double saturation, double value)
    {
        var sector = hue * 6d;
        var index = (int)Math.Floor(sector) % 6;
        var fraction = sector - Math.Floor(sector);
        var p = value * (1d - saturation);
        var q = value * (1d - fraction * saturation);
        var t = value * (1d - (1d - fraction) * saturation);
        var rgb = index switch
        {
            0 => (value, t, p),
            1 => (q, value, p),
            2 => (p, value, t),
            3 => (p, q, value),
            4 => (t, p, value),
            _ => (value, p, q)
        };
        return (ToByte(rgb.Item1 * 255d), ToByte(rgb.Item2 * 255d), ToByte(rgb.Item3 * 255d));
    }
}
