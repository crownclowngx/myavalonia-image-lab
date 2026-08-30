using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Domain.Wavelets;

/// <summary>拥有一份连续 packed 系数缓冲及其层布局的不可变领域值。</summary>
/// <remarks>
/// 构造函数复制外部数组，公开成员只返回只读内存。阈值处理通过 <see cref="CloneCoefficients"/>
/// 获得私有工作副本并创建新金字塔，因此基线结果可以安全地被 UI、扫描和水印实验同时读取。
/// </remarks>
internal sealed class WaveletPyramid
{
    private readonly double[] _coefficients;
    private readonly WaveletLevelDescriptor[] _levels;

    public WaveletPyramid(
        WaveletTransformId transform,
        ImageChannel channel,
        ImageSize originalSize,
        ImageSize paddedSize,
        ReadOnlySpan<double> coefficients,
        IEnumerable<WaveletLevelDescriptor> levels)
    {
        if (coefficients.Length != paddedSize.PixelCount)
            throw new ArgumentException("系数数量必须与扩展平面尺寸一致。", nameof(coefficients));
        if (((ReadOnlySpan<double>)coefficients).ContainsAnyExceptFinite())
            throw new ArgumentException("系数缓冲包含 NaN 或无穷值。", nameof(coefficients));
        _levels = levels.OrderBy(level => level.Level).ToArray();
        if (_levels.Length is < 1 or > WaveletLimits.MaximumLevels ||
            _levels.Select((value, index) => value.Level == index + 1).Any(value => !value))
            throw new ArgumentException("金字塔层号必须从 1 连续递增且不超过 6。", nameof(levels));

        Transform = transform;
        Channel = channel;
        OriginalSize = originalSize;
        PaddedSize = paddedSize;
        _coefficients = coefficients.ToArray();
        ValidateLayout();
    }

    public WaveletTransformId Transform { get; }
    public ImageChannel Channel { get; }
    public ImageSize OriginalSize { get; }
    public ImageSize PaddedSize { get; }
    public IReadOnlyList<WaveletLevelDescriptor> Levels => _levels;
    public ReadOnlyMemory<double> Coefficients => _coefficients;

    public WaveletLevelDescriptor GetLevel(int level) => level is >= 1 && level <= _levels.Length
        ? _levels[level - 1]
        : throw new ArgumentOutOfRangeException(nameof(level), "层号超出金字塔范围。");

    internal double[] CloneCoefficients() => (double[])_coefficients.Clone();

    private void ValidateLayout()
    {
        foreach (var level in _levels)
        {
            if ((level.ActiveWidth & 1) != 0 || (level.ActiveHeight & 1) != 0)
                throw new ArgumentException($"第 {level.Level} 层有效尺寸必须为偶数。");
            var halfWidth = level.ActiveWidth / 2;
            var halfHeight = level.ActiveHeight / 2;
            var expected = new[]
            {
                new WaveletRegion(0, 0, halfWidth, halfHeight),
                new WaveletRegion(0, halfHeight, halfWidth, halfHeight),
                new WaveletRegion(halfWidth, 0, halfWidth, halfHeight),
                new WaveletRegion(halfWidth, halfHeight, halfWidth, halfHeight)
            };
            var actual = new[] { level.Approximation, level.HorizontalDetail, level.VerticalDetail, level.DiagonalDetail };
            if (!actual.SequenceEqual(expected) || actual.Any(region => region.Right > PaddedSize.Width || region.Bottom > PaddedSize.Height))
                throw new ArgumentException($"第 {level.Level} 层 packed 布局不符合冻结的四象限协议。");
        }
    }
}

internal static class WaveletFiniteExtensions
{
    public static bool ContainsAnyExceptFinite(this ReadOnlySpan<double> values)
    {
        foreach (var value in values)
            if (!double.IsFinite(value)) return true;
        return false;
    }
}
