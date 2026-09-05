using ImageLabPlugin.Domain.Shared.Spectral;
using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Domain.FrequencyFiltering;

/// <summary>计算单个归一化半径处的滤波振幅增益及 90%–10% 过渡区。</summary>
/// <remarks>
/// 这里的 H=0.5 是复频谱乘数的“振幅增益”，不是功率意义的 -3 dB。三种固定公式没有独立生命周期或
/// 运行时扩展需求，因此朴素的完整 switch 比 Strategy/抽象工厂更直接，也让十二种组合的数值边界集中可审查。
/// </remarks>
internal sealed class RadialFilterResponse
{
    private static readonly double LogTwo = Math.Log(2d);

    public double Evaluate(FrequencyFilterRecipe recipe, double radius)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        if (!double.IsFinite(radius) || radius is < 0d or > 1d)
            throw new ArgumentOutOfRangeException(nameof(radius), "归一化半径必须位于 [0,1]。");
        var innerLowPass = LowPass(recipe.Family, radius, recipe.InnerCutoff, recipe.ButterworthOrder);
        var gain = recipe.Kind switch
        {
            FrequencyFilterKind.LowPass => innerLowPass,
            FrequencyFilterKind.HighPass => 1d - innerLowPass,
            FrequencyFilterKind.BandPass => (1d - innerLowPass) *
                LowPass(recipe.Family, radius, recipe.OuterCutoff, recipe.ButterworthOrder),
            // 带阻被定义为同参数带通的逐点补集，而不是再造一套边界公式；这固定了两者和为 1 的契约。
            FrequencyFilterKind.BandStop => 1d - ((1d - innerLowPass) *
                LowPass(recipe.Family, radius, recipe.OuterCutoff, recipe.ButterworthOrder)),
            _ => throw new ArgumentOutOfRangeException(nameof(recipe))
        };
        return Math.Clamp(double.IsFinite(gain) ? gain : 0d, 0d, 1d);
    }

    public FrequencyTransitionBand Transition(FrequencyFilterFamily family, double cutoff, int order)
    {
        if (!double.IsFinite(cutoff) || cutoff <= 0d || cutoff > 1d) throw new ArgumentOutOfRangeException(nameof(cutoff));
        if (family == FrequencyFilterFamily.Butterworth && order is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(order));
        if (family == FrequencyFilterFamily.Ideal) return new FrequencyTransitionBand(cutoff, cutoff);
        var r90 = RadiusAtGain(family, cutoff, order, 0.9d);
        var r10 = RadiusAtGain(family, cutoff, order, 0.1d);
        return new FrequencyTransitionBand(Math.Clamp(r90, 0d, 1d), Math.Clamp(r10, 0d, 1d));
    }

    private static double LowPass(FrequencyFilterFamily family, double radius, double cutoff, int order)
    {
        if (radius == 0d) return 1d; // 显式处理 DC，避免未来公式重排把 0/0 带入频谱。
        return family switch
        {
            FrequencyFilterFamily.Ideal => radius <= cutoff ? 1d : 0d,
            FrequencyFilterFamily.Butterworth => Butterworth(radius, cutoff, order),
            // exp 在极端处下溢到 0 是有效阻带，不会产生负值或 NaN。
            FrequencyFilterFamily.Gaussian => Math.Exp(-LogTwo * Math.Pow(radius / cutoff, 2d)),
            _ => throw new ArgumentOutOfRangeException(nameof(family), family, "未知滤波家族。")
        };
    }

    private static double Butterworth(double radius, double cutoff, int order)
    {
        // 使用对数域判断，避免 (r/c)^(2n) 在高阶和小截止时溢出；巨大比值的极限增益就是 0。
        var exponent = 2d * order * Math.Log(radius / cutoff);
        if (exponent > 709d) return 0d;
        if (exponent < -745d) return 1d;
        return 1d / (1d + Math.Exp(exponent));
    }

    private static double RadiusAtGain(FrequencyFilterFamily family, double cutoff, int order, double gain) => family switch
    {
        FrequencyFilterFamily.Butterworth => cutoff * Math.Pow((1d / gain) - 1d, 1d / (2d * order)),
        FrequencyFilterFamily.Gaussian => cutoff * Math.Sqrt(-Math.Log(gain) / LogTwo),
        _ => cutoff
    };
}

/// <summary>在统一 FFT 坐标上生成 double 增益遮罩与灰度预览。</summary>
internal sealed class FrequencyFilterMaskFactory(RadialFilterResponse response)
{
    public const int DefaultRadialSampleCount = 257;

    public FrequencyFilterMask Create(FrequencySpectrum spectrum, FrequencyFilterRecipe recipe,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spectrum); ArgumentNullException.ThrowIfNull(recipe);
        var gains = new double[checked(spectrum.PaddedWidth * spectrum.PaddedHeight)];
        for (var y = 0; y < spectrum.PaddedHeight; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < spectrum.PaddedWidth; x++)
            {
                var radius = FrequencyCoordinates.FromInternal(x, y, spectrum.PaddedWidth, spectrum.PaddedHeight).Radius;
                gains[(y * spectrum.PaddedWidth) + x] = response.Evaluate(recipe, radius);
            }
        }
        var radial = new RadialResponseSample[DefaultRadialSampleCount];
        for (var i = 0; i < radial.Length; i++)
        {
            var radius = i / (double)(radial.Length - 1);
            radial[i] = new RadialResponseSample(radius, response.Evaluate(recipe, radius));
        }
        return new FrequencyFilterMask(spectrum.PaddedWidth, spectrum.PaddedHeight, gains, radial, recipe.MathematicalFingerprint());
    }

    public PixelImage CreatePreview(FrequencyFilterMask mask)
    {
        ArgumentNullException.ThrowIfNull(mask);
        var rgba = new byte[checked(mask.Width * mask.Height * 4)];
        for (var displayY = 0; displayY < mask.Height; displayY++)
            for (var displayX = 0; displayX < mask.Width; displayX++)
            {
                var point = FrequencyCoordinates.FromDisplay(displayX, displayY, mask.Width, mask.Height);
                var level = (byte)Math.Clamp((int)Math.Round(mask[point.InternalX, point.InternalY] * 255d), 0, 255);
                var offset = ((displayY * mask.Width) + displayX) * 4;
                rgba[offset] = rgba[offset + 1] = rgba[offset + 2] = level; rgba[offset + 3] = 255;
            }
        return new PixelImage(new ImageSize(mask.Width, mask.Height), rgba);
    }
}
