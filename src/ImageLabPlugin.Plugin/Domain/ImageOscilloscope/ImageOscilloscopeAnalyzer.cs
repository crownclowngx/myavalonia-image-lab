using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Domain.ImageOscilloscope;

/// <summary>以一次行优先全像素扫描累计 Waveform、Parade、Vectorscope、直方图和颜色分布。</summary>
/// <remarks>
/// 栅格尺寸在扫描前用 checked 验证，并与源像素数解耦；每个像素恰好向每类计数贡献一次。
/// 循环至少每行检查取消，不创建逐像素 Y/Cb/Cr/HSV 缓存，也不建立从 bin 反查源像素的索引。
/// </remarks>
internal sealed class ImageOscilloscopeAnalyzer(OscilloscopeColorConverter converter)
{
    public const int MaximumWaveformWidth = 1024;
    public const int ScopeHeight = 256;
    public const int VectorscopeSize = 512;
    public const long MaximumGridBytes = 5L * 1024 * 1024;
    private static readonly double MaximumChromaRadius = Math.Sqrt(0.5d);

    public ImageOscilloscopeAnalysis Analyze(PixelImage source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var width = Math.Min(source.Size.Width, MaximumWaveformWidth);
        EnsureGridBudget(width);
        var cells = checked(width * ScopeHeight);
        var waveform = new uint[cells];
        var redParade = new uint[cells];
        var greenParade = new uint[cells];
        var blueParade = new uint[cells];
        var vectorscope = new uint[checked(VectorscopeSize * VectorscopeSize)];
        var redHistogram = new ulong[256]; var greenHistogram = new ulong[256];
        var blueHistogram = new ulong[256]; var lumaHistogram = new ulong[256];
        var saturationHistogram = new ulong[256]; var hueWeights = new double[360];
        var chromaHistogram = new ulong[256];
        ulong hueDefined = 0;
        double cbSum = 0d, crSum = 0d, chromaSum = 0d;
        var rgba = source.Rgba.Span;

        for (var y = 0; y < source.Size.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < source.Size.Width; x++)
            {
                var offset = checked(((y * source.Size.Width) + x) * 4);
                var pixel = converter.Convert(rgba[offset], rgba[offset + 1], rgba[offset + 2], rgba[offset + 3]);
                var scopeX = MapHorizontal(x, source.Size.Width, width);
                waveform[checked(((255 - pixel.Luma) * width) + scopeX)]++;
                redParade[checked(((255 - pixel.Red) * width) + scopeX)]++;
                greenParade[checked(((255 - pixel.Green) * width) + scopeX)]++;
                blueParade[checked(((255 - pixel.Blue) * width) + scopeX)]++;
                var vector = MapVectorscope(pixel.Cb, pixel.Cr);
                vectorscope[checked((vector.Y * VectorscopeSize) + vector.X)]++;
                redHistogram[pixel.Red]++; greenHistogram[pixel.Green]++;
                blueHistogram[pixel.Blue]++; lumaHistogram[pixel.Luma]++;
                saturationHistogram[QuantizeUnit(pixel.Saturation)]++;
                if (pixel.Hue is { } hue)
                {
                    hueDefined++;
                    hueWeights[Math.Min(359, (int)Math.Floor(hue))] += pixel.Saturation;
                }
                var chroma = pixel.ChromaRadius;
                chromaHistogram[QuantizeUnit(chroma / MaximumChromaRadius)]++;
                cbSum += pixel.Cb; crSum += pixel.Cr; chromaSum += chroma;
            }
        }

        var pixelCount = source.Size.PixelCount;
        return new ImageOscilloscopeAnalysis(source.Size,
            new ScopeCountGrid(width, ScopeHeight, waveform),
            new ScopeCountGrid(width, ScopeHeight, redParade),
            new ScopeCountGrid(width, ScopeHeight, greenParade),
            new ScopeCountGrid(width, ScopeHeight, blueParade),
            new ScopeCountGrid(VectorscopeSize, VectorscopeSize, vectorscope),
            redHistogram, greenHistogram, blueHistogram, lumaHistogram,
            saturationHistogram, hueWeights, hueDefined, chromaHistogram,
            cbSum / pixelCount, crSum / pixelCount, chromaSum / pixelCount);
    }

    public static int MapHorizontal(int sourceX, int sourceWidth, int scopeWidth)
    {
        if ((uint)sourceX >= (uint)sourceWidth) throw new ArgumentOutOfRangeException(nameof(sourceX));
        return (int)(((long)sourceX * scopeWidth) / sourceWidth);
    }

    public static ScopePoint MapVectorscope(double cb, double cr)
    {
        if (!double.IsFinite(cb) || !double.IsFinite(cr)) throw new ArgumentOutOfRangeException(nameof(cb));
        var x = (int)Math.Round((Math.Clamp(cb, -0.5d, 0.5d) + 0.5d) * (VectorscopeSize - 1), MidpointRounding.ToEven);
        var y = (int)Math.Round((0.5d - Math.Clamp(cr, -0.5d, 0.5d)) * (VectorscopeSize - 1), MidpointRounding.ToEven);
        return new ScopePoint(Math.Clamp(x, 0, VectorscopeSize - 1), Math.Clamp(y, 0, VectorscopeSize - 1));
    }

    internal static int QuantizeUnit(double value) => Math.Clamp(
        (int)Math.Round(Math.Clamp(value, 0d, 1d) * 255d, MidpointRounding.ToEven), 0, 255);

    private static void EnsureGridBudget(int waveformWidth)
    {
        var bytes = checked((((long)waveformWidth * ScopeHeight * 4L) * 4L) +
                            ((long)VectorscopeSize * VectorscopeSize * 4L));
        if (bytes > MaximumGridBytes)
            throw new InvalidOperationException($"示波器固定计数栅格需要 {bytes:N0} 字节，超过结构预算。");
    }
}
