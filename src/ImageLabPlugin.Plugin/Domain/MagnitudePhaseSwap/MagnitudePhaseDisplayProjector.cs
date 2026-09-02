using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Domain.MagnitudePhaseSwap;

internal sealed record MagnitudePhaseProjectionStatistics(double RawMinimum, double RawMaximum, double RawMean,
    long ClippedLowCount, long ClippedHighCount, double ScientificAbsolutePercentile);

internal sealed record MagnitudePhaseProjectionResult(PixelImage Image,
    MagnitudePhaseProjectionKind Kind, MagnitudePhaseProjectionStatistics Statistics, string? DiagnosticLabel);

/// <summary>把 raw 实值平面转换成物理裁切或固定规则的诊断显示。</summary>
/// <remarks>
/// 普通结果使用 ToEven 舍入并裁切到 [0,255]，不会自动拉伸对比度。phase-only 不保留原亮度量纲，
/// 因而使用零中心、P99.5 绝对值尺度的科学投影，并携带强制标签；这类结果不得计算 PSNR/SSIM。
/// </remarks>
internal sealed class MagnitudePhaseDisplayProjector
{
    public MagnitudePhaseProjectionResult Project(MagnitudePhaseRawResult raw,
        MagnitudePhaseProjectionKind kind, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        var values = raw.Values.Span;
        double minimum = double.PositiveInfinity, maximum = double.NegativeInfinity, sum = 0d;
        foreach (var value in values) { minimum = Math.Min(minimum, value); maximum = Math.Max(maximum, value); sum += value; }
        var percentile = kind == MagnitudePhaseProjectionKind.SignedScientific ? AbsolutePercentile(values, .995d) : 0d;
        var rgba = new byte[checked(values.Length * 4)];
        long low = 0, high = 0;
        for (var y = 0; y < raw.Size; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < raw.Size; x++)
            {
                var index = (y * raw.Size) + x;
                var value = values[index];
                byte level;
                if (kind == MagnitudePhaseProjectionKind.PhysicalClamp)
                {
                    low += value < 0d ? 1 : 0;
                    high += value > 255d ? 1 : 0;
                    level = (byte)Math.Clamp((int)Math.Round(value, MidpointRounding.ToEven), 0, 255);
                }
                else
                {
                    var normalized = percentile <= 0d ? 0d : Math.Clamp(value / percentile, -1d, 1d);
                    level = (byte)Math.Clamp((int)Math.Round(127.5d + (127.5d * normalized), MidpointRounding.ToEven), 0, 255);
                }
                rgba[index * 4] = rgba[(index * 4) + 1] = rgba[(index * 4) + 2] = level;
                rgba[(index * 4) + 3] = 255;
            }
        }
        return new MagnitudePhaseProjectionResult(new PixelImage(new ImageSize(raw.Size, raw.Size), rgba), kind,
            new MagnitudePhaseProjectionStatistics(minimum, maximum, sum / values.Length, low, high, percentile),
            kind == MagnitudePhaseProjectionKind.SignedScientific ? "诊断显示，不保留原亮度量纲" : null);
    }

    private static double AbsolutePercentile(ReadOnlySpan<double> values, double percentile)
    {
        var sorted = new double[values.Length];
        for (var i = 0; i < values.Length; i++) sorted[i] = Math.Abs(values[i]);
        Array.Sort(sorted);
        var index = Math.Clamp((int)Math.Ceiling(percentile * sorted.Length) - 1, 0, sorted.Length - 1);
        return sorted[index];
    }
}
