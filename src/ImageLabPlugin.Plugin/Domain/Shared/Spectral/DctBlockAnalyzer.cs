using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Domain.Shared.Spectral;

internal readonly record struct ImagePoint(int X, int Y);
internal sealed record DctBlockReport(
    ImagePoint Origin,
    ImageChannel Channel,
    IReadOnlyList<double> Pixels,
    IReadOnlyList<double> Coefficients,
    IReadOnlyList<double> Reconstructed,
    IReadOnlyList<double> AbsoluteErrors,
    double MaximumError,
    string? UnavailableReason)
{
    public bool IsAvailable => UnavailableReason is null;
}

/// <summary>检查源图坐标所在的一个完整 8×8 通道块。</summary>
internal sealed class DctBlockAnalyzer(ImageChannelConverter channelConverter, Dct8x8Transform transform)
{
    public DctBlockReport Analyze(PixelImage image, ImageChannel channel, ImagePoint point)
    {
        ArgumentNullException.ThrowIfNull(image);
        if ((uint)point.X >= (uint)image.Size.Width || (uint)point.Y >= (uint)image.Size.Height)
            throw new ArgumentOutOfRangeException(nameof(point), "图片选择坐标越界。 ");
        var origin = new ImagePoint((point.X / 8) * 8, (point.Y / 8) * 8);
        if (origin.X + 8 > image.Size.Width || origin.Y + 8 > image.Size.Height)
            return new DctBlockReport(origin, channel, [], [], [], [], 0d, "所选位置属于非完整 DCT 块，V1 不补零或移动块。 ");

        var plane = channelConverter.Extract(image, channel);
        var pixels = new double[64];
        var coefficients = new double[64];
        var reconstructed = new double[64];
        var errors = new double[64];
        for (var y = 0; y < 8; y++)
            for (var x = 0; x < 8; x++) pixels[(y * 8) + x] = plane[origin.X + x, origin.Y + y];
        transform.Forward(pixels, coefficients);
        transform.Inverse(coefficients, reconstructed);
        double maximum = 0d;
        for (var i = 0; i < 64; i++) { errors[i] = Math.Abs(pixels[i] - reconstructed[i]); maximum = Math.Max(maximum, errors[i]); }
        return new DctBlockReport(origin, channel, pixels, coefficients, reconstructed, errors, maximum, null);
    }

    public static FrequencyRegion ClassifyCoefficient(int u, int v)
    {
        if ((uint)u >= 8 || (uint)v >= 8) throw new ArgumentOutOfRangeException(nameof(u));
        var sum = u + v;
        return sum == 0 ? FrequencyRegion.Dc : sum <= 3 ? FrequencyRegion.Low : sum <= 7 ? FrequencyRegion.Medium : FrequencyRegion.High;
    }
}
