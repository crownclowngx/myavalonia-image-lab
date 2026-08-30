using System.Numerics;
using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Domain.Frequency;

/// <summary>拥有一份不可从外部修改的二维复数频谱及其补零语义。</summary>
internal sealed class FrequencySpectrum
{
    public const int MaximumComplexValues = 2048 * 2048;
    private readonly Complex[] _values;

    public FrequencySpectrum(ImageSize sourceSize, int paddedWidth, int paddedHeight, ReadOnlySpan<Complex> values)
    {
        if (paddedWidth < sourceSize.Width || paddedHeight < sourceSize.Height || paddedWidth > 2048 || paddedHeight > 2048 ||
            (paddedWidth & (paddedWidth - 1)) != 0 || (paddedHeight & (paddedHeight - 1)) != 0 ||
            values.Length != checked(paddedWidth * paddedHeight) || values.Length > MaximumComplexValues)
        {
            throw new ArgumentException("频谱尺寸、补零信息或资源上限不合法。", nameof(values));
        }

        SourceSize = sourceSize;
        PaddedWidth = paddedWidth;
        PaddedHeight = paddedHeight;
        _values = values.ToArray();
    }

    public ImageSize SourceSize { get; }
    public int PaddedWidth { get; }
    public int PaddedHeight { get; }
    public int ValueCount => _values.Length;
    public Complex this[int x, int y] => _values[(y * PaddedWidth) + x];
    public ReadOnlyMemory<Complex> Values => _values;
    internal Complex[] CreateWorkingCopy() => (Complex[])_values.Clone();

    public static int NextPowerOfTwo(int value)
    {
        if (value <= 0 || value > 2048) throw new ArgumentOutOfRangeException(nameof(value));
        var result = 1;
        while (result < value) result <<= 1;
        return result;
    }
}
