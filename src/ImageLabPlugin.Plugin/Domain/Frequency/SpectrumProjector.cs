using System.Numerics;
using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Domain.Frequency;

internal enum SpectrumMagnitudeMode { Linear, Logarithmic, Percentile }
internal enum SpectrumViewMode { Magnitude, Phase, Dct }

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
        var limit = ResolveMagnitudeLimit(spectrum.Values.Span, mode);
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
            0 => (value, t, p), 1 => (q, value, p), 2 => (p, value, t),
            3 => (p, q, value), 4 => (t, p, value), _ => (value, p, q)
        };
        return (ToByte(rgb.Item1 * 255d), ToByte(rgb.Item2 * 255d), ToByte(rgb.Item3 * 255d));
    }
}
