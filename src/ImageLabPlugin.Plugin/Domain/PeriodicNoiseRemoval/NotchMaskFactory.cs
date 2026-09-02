using ImageLabPlugin.Domain.Shared.Spectral;
using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Domain.PeriodicNoiseRemoval;

internal sealed record PeriodicNotchMaskStatistics(double MinimumGain, double MaximumGain, double MeanGain,
    int ModifiedBinCount, double ModifiedBinRatio);

/// <summary>拥有共享共轭安全增益遮罩及其有限统计。</summary>
internal sealed record PeriodicNotchMask(FrequencyGainMask GainMask, PeriodicNotchMaskStatistics Statistics,
    PixelImage Preview);

/// <summary>把不可变周期陷波配方光栅化为共轭安全的共享增益遮罩。</summary>
/// <remarks>
/// 每个频点对所有启用中心及其共轭中心取最小增益，避免重叠陷波产生隐藏乘法叠加。循环按自然共轭对只计算一次并把同值
/// 写入两侧，随后仍交给 <see cref="FrequencyGainMask"/> 执行 1E-12 二次门禁；导入数据永远不能直接提供逐 bin 增益。
/// </remarks>
internal sealed class NotchMaskFactory(NotchResponse response)
{
    public PeriodicNotchMask Create(FrequencySpectrum spectrum, PeriodicNoiseRecipe recipe,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spectrum);
        ArgumentNullException.ThrowIfNull(recipe);
        var width = spectrum.PaddedWidth;
        var height = spectrum.PaddedHeight;
        var gains = new double[checked(width * height)];
        Array.Fill(gains, 1d);
        var enabled = recipe.Notches.Where(item => item.Enabled).ToArray();
        for (var y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < width; x++)
            {
                var index = (y * width) + x;
                var conjugate = FrequencyCoordinates.ConjugateIndex(x, y, width, height);
                var conjugateIndex = (conjugate.Y * width) + conjugate.X;
                if (index > conjugateIndex) continue;
                var point = PeriodicFrequency.FromInternal(x, y, width, height);
                var gain = 1d;
                foreach (var notch in enabled)
                {
                    var distance = Math.Min(
                        PeriodicFrequency.ToroidalDistance(point, notch.CanonicalFrequency),
                        PeriodicFrequency.ToroidalDistance(point, notch.CanonicalFrequency.Conjugate()));
                    gain = Math.Min(gain, response.Gain(distance, recipe.Transition, recipe.Radius,
                        recipe.Strength, recipe.ButterworthOrder));
                }
                gains[index] = gain;
                gains[conjugateIndex] = gain;
            }
        }

        double minimum = 1d, maximum = 0d, sum = 0d;
        var modified = 0;
        var rgba = new byte[gains.Length * 4];
        for (var i = 0; i < gains.Length; i++)
        {
            var gain = gains[i];
            minimum = Math.Min(minimum, gain);
            maximum = Math.Max(maximum, gain);
            sum += gain;
            if (gain < 1d - 1e-12) modified++;
            var level = (byte)Math.Clamp((int)Math.Round(gain * 255d), 0, 255);
            rgba[(i * 4)] = rgba[(i * 4) + 1] = rgba[(i * 4) + 2] = level;
            rgba[(i * 4) + 3] = 255;
        }
        var mask = new FrequencyGainMask(width, height, gains, recipe.MathematicalFingerprint());
        return new PeriodicNotchMask(mask,
            new PeriodicNotchMaskStatistics(minimum, maximum, sum / gains.Length, modified,
                modified / (double)gains.Length),
            CreateCenteredPreview(width, height, rgba));
    }

    private static PixelImage CreateCenteredPreview(int width, int height, ReadOnlySpan<byte> natural)
    {
        var centered = new byte[natural.Length];
        for (var displayY = 0; displayY < height; displayY++)
        for (var displayX = 0; displayX < width; displayX++)
        {
            var point = FrequencyCoordinates.FromDisplay(displayX, displayY, width, height);
            natural.Slice(((point.InternalY * width) + point.InternalX) * 4, 4)
                .CopyTo(centered.AsSpan(((displayY * width) + displayX) * 4, 4));
        }
        return new PixelImage(new ImageSize(width, height), centered);
    }
}
