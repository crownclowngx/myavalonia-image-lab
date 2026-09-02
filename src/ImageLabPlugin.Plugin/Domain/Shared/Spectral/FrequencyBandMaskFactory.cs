using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Domain.Shared.Spectral;

internal enum FrequencyBandKind { All, Low, Medium, High, Custom }

internal readonly record struct FrequencyBandDefinition
{
    public FrequencyBandDefinition(FrequencyBandKind kind, FrequencyBandBoundaries boundaries, double inner = 0d, double outer = 1d)
    {
        if (kind == FrequencyBandKind.Custom && !(inner >= 0d && inner < outer && outer <= 1d))
            throw new ArgumentOutOfRangeException(nameof(inner), "自定义频带必须满足 0 ≤ inner < outer ≤ 1。 ");
        Kind = kind; Boundaries = boundaries; Inner = inner; Outer = outer;
    }
    public FrequencyBandKind Kind { get; }
    public FrequencyBandBoundaries Boundaries { get; }
    public double Inner { get; }
    public double Outer { get; }
    public bool Includes(double radius) => Kind switch
    {
        FrequencyBandKind.All => true,
        FrequencyBandKind.Low => radius <= Boundaries.Low,
        FrequencyBandKind.Medium => radius > Boundaries.Low && radius <= Boundaries.High,
        FrequencyBandKind.High => radius > Boundaries.High,
        FrequencyBandKind.Custom => radius >= Inner && radius <= Outer,
        _ => false
    };
}

/// <summary>生成固定 0/1 的共轭对称径向遮罩和可显示预览。</summary>
internal sealed class FrequencyBandMaskFactory
{
    public byte[] Create(FrequencySpectrum spectrum, FrequencyBandDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spectrum);
        var mask = new byte[spectrum.ValueCount];
        for (var y = 0; y < spectrum.PaddedHeight; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < spectrum.PaddedWidth; x++)
            {
                var point = FrequencyCoordinates.FromInternal(x, y, spectrum.PaddedWidth, spectrum.PaddedHeight);
                mask[(y * spectrum.PaddedWidth) + x] = definition.Includes(point.Radius) ? (byte)1 : (byte)0;
            }
        }
        return mask;
    }

    public PixelImage CreatePreview(FrequencySpectrum spectrum, ReadOnlySpan<byte> mask)
    {
        if (mask.Length != spectrum.ValueCount) throw new ArgumentException("遮罩尺寸与频谱不一致。", nameof(mask));
        var rgba = new byte[checked(mask.Length * 4)];
        for (var displayY = 0; displayY < spectrum.PaddedHeight; displayY++)
        for (var displayX = 0; displayX < spectrum.PaddedWidth; displayX++)
        {
            var point = FrequencyCoordinates.FromDisplay(displayX, displayY, spectrum.PaddedWidth, spectrum.PaddedHeight);
            var level = mask[(point.InternalY * spectrum.PaddedWidth) + point.InternalX] == 0 ? (byte)0 : (byte)255;
            var offset = ((displayY * spectrum.PaddedWidth) + displayX) * 4;
            rgba[offset] = rgba[offset + 1] = rgba[offset + 2] = level; rgba[offset + 3] = 255;
        }
        return new PixelImage(new ImageSize(spectrum.PaddedWidth, spectrum.PaddedHeight), rgba);
    }
}
