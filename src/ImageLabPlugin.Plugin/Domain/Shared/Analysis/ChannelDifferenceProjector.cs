using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Domain.Shared.Analysis;

internal sealed record ChannelDifferenceProjection(
    PixelImage Signed,
    PixelImage Absolute,
    double MaximumAbsoluteDifference);

/// <summary>把任意同尺寸通道差异投影为同时可辨方向和幅值的两张图片。</summary>
internal sealed class ChannelDifferenceProjector
{
    public ChannelDifferenceProjection Project(
        ImageChannelPlane source,
        ImageChannelPlane result,
        double amplification = 4d,
        CancellationToken cancellationToken = default)
    {
        if (source.Size != result.Size)
            throw new ArgumentException("差异平面尺寸必须一致。");
        if (!double.IsFinite(amplification) || amplification <= 0d || amplification > 32d)
            throw new ArgumentOutOfRangeException(nameof(amplification));

        var a = source.Values.Span;
        var b = result.Values.Span;
        var signed = new byte[checked(a.Length * 4)];
        var absolute = new byte[signed.Length];
        double maximum = 0d;
        for (var i = 0; i < a.Length; i++)
        {
            if ((i & 16383) == 0) cancellationToken.ThrowIfCancellationRequested();
            var delta = b[i] - a[i];
            maximum = Math.Max(maximum, Math.Abs(delta));
            var offset = i * 4;
            var level = (byte)Math.Clamp((int)Math.Round(Math.Abs(delta) * amplification), 0, 255);
            signed[offset] = delta >= 0d ? level : (byte)0;
            signed[offset + 1] = 0;
            signed[offset + 2] = delta < 0d ? level : (byte)0;
            signed[offset + 3] = 255;
            absolute[offset] = absolute[offset + 1] = absolute[offset + 2] = level;
            absolute[offset + 3] = 255;
        }

        return new ChannelDifferenceProjection(
            new PixelImage(source.Size, signed),
            new PixelImage(source.Size, absolute),
            maximum);
    }
}
