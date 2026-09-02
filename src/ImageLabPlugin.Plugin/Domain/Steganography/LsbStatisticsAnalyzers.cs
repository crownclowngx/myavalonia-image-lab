using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Domain.Steganography;

/// <summary>在完全相同的通道、bit、坐标和 Scope 上计算 cover/stego 的教学统计。</summary>
/// <remarks>
/// 位分布、PoV 卡方与邻接转移是三个独立观察角度，不合成为“隐写概率”。RGB 策略按逻辑槽位
/// 汇总真实样本数，不先平均成灰度。热点循环只使用数值数组和集合，不产生逐像素 DTO。
/// </remarks>
internal sealed class LsbStatisticsAnalyzer
{
    public LsbStatisticsComparison Compare(
        PixelImage cover,
        PixelImage stego,
        LsbSlotLayout layout,
        LsbRecipe recipe,
        IReadOnlyList<int> selectedSlots,
        LsbStatisticsScope scope,
        CancellationToken token)
    {
        if (cover.Size != stego.Size || cover.Size != layout.Size) throw new ArgumentException("统计比较要求同尺寸图片和同一槽位布局。");
        if (scope == LsbStatisticsScope.SequentialPrefix && recipe.Placement != LsbPlacementKind.Sequential)
            throw new ArgumentException("SequentialPrefix 只适用于顺序写入。", nameof(scope));
        var selector = new ScopeSelector(layout.GetEligibleSlotCount(recipe.Channels), selectedSlots, scope);
        var left = AnalyzeOne(cover, layout, recipe, selector, scope, token);
        var right = AnalyzeOne(stego, layout, recipe, selector, scope, token);
        if (left.SampleCount != right.SampleCount) throw new InvalidOperationException("cover/stego 样本数不同，拒绝计算差值。");
        var byChannel = BuildChannelBreakdown(cover, stego, layout, recipe, selectedSlots, scope, token, left, right);
        return new(left, right, byChannel);
    }

    private static IReadOnlyDictionary<LsbChannel, LsbChannelStatisticsComparison> BuildChannelBreakdown(
        PixelImage cover,
        PixelImage stego,
        LsbSlotLayout layout,
        LsbRecipe recipe,
        IReadOnlyList<int> selectedSlots,
        LsbStatisticsScope scope,
        CancellationToken token,
        LsbStatistics aggregateCover,
        LsbStatistics aggregateStego)
    {
        if (recipe.Channels != LsbChannelStrategy.RgbRoundRobin)
        {
            var channel = recipe.Channels switch { LsbChannelStrategy.Red => LsbChannel.Red, LsbChannelStrategy.Green => LsbChannel.Green, _ => LsbChannel.Blue };
            return new Dictionary<LsbChannel, LsbChannelStatisticsComparison> { [channel] = new(aggregateCover, aggregateStego) };
        }
        var result = new Dictionary<LsbChannel, LsbChannelStatisticsComparison>();
        foreach (var channel in new[] { LsbChannel.Red, LsbChannel.Green, LsbChannel.Blue })
        {
            var channelStrategy = channel switch { LsbChannel.Red => LsbChannelStrategy.Red, LsbChannel.Green => LsbChannelStrategy.Green, _ => LsbChannelStrategy.Blue };
            var channelRecipe = recipe with { Channels = channelStrategy };
            var channelSelected = selectedSlots.Where(value => value % 3 == (int)channel).Select(value => value / 3).ToArray();
            var selector = new ScopeSelector(layout.GetEligibleSlotCount(channelStrategy), channelSelected, scope);
            var channelCover = AnalyzeOne(cover, layout, channelRecipe, selector, scope, token);
            var channelStego = AnalyzeOne(stego, layout, channelRecipe, selector, scope, token);
            result[channel] = new(channelCover, channelStego);
        }
        return result;
    }

    private static LsbStatistics AnalyzeOne(
        PixelImage image,
        LsbSlotLayout layout,
        LsbRecipe recipe,
        ScopeSelector selector,
        LsbStatisticsScope scope,
        CancellationToken token)
    {
        var bytes = image.Rgba.Span;
        var frequencies = new long[256];
        long zeros = 0, ones = 0;
        var total = layout.GetEligibleSlotCount(recipe.Channels);
        for (var logical = 0; logical < total; logical++)
        {
            if ((logical & 0x3fff) == 0) token.ThrowIfCancellationRequested();
            if (!selector.Contains(logical)) continue;
            var value = bytes[layout.Resolve(logical, recipe.Channels).RgbaOffset];
            frequencies[value]++;
            if (((value >> recipe.BitPlane) & 1) == 0) zeros++; else ones++;
        }

        var samples = zeros + ones;
        double? ratio = samples == 0 ? null : ones / (double)samples;
        double? entropy = ratio is null ? null : BinaryEntropy(ratio.Value);
        var chi = ComputeChiSquare(frequencies, recipe.BitPlane, samples);
        var horizontal = CountAdjacency(image, layout, recipe, selector, horizontal: true, token);
        var vertical = CountAdjacency(image, layout, recipe, selector, horizontal: false, token);
        return new(scope, samples, new(zeros, ones, ratio, entropy), chi, horizontal, vertical);
    }

    private static LsbChiSquare ComputeChiSquare(long[] frequencies, int bitPlane, long samples)
    {
        var mask = 1 << bitPlane;
        var chi = 0d;
        var degrees = 0;
        for (var value = 0; value < 256; value++)
        {
            if ((value & mask) != 0) continue;
            var first = frequencies[value];
            var second = frequencies[value ^ mask];
            var expected = (first + second) / 2d;
            if (expected == 0) continue;
            var delta = first - expected;
            chi += (delta * delta / expected) * 2d;
            degrees++;
        }
        return new(chi, degrees, degrees == 0 ? null : RegularizedGamma.Upper(degrees / 2d, chi / 2d), samples);
    }

    private static LsbAdjacency CountAdjacency(
        PixelImage image,
        LsbSlotLayout layout,
        LsbRecipe recipe,
        ScopeSelector selector,
        bool horizontal,
        CancellationToken token)
    {
        long c00 = 0, c01 = 0, c10 = 0, c11 = 0;
        var width = image.Size.Width;
        var height = image.Size.Height;
        var bytes = image.Rgba.Span;
        var channels = recipe.Channels == LsbChannelStrategy.RgbRoundRobin
            ? new[] { LsbChannel.Red, LsbChannel.Green, LsbChannel.Blue }
            : new[] { recipe.Channels switch { LsbChannelStrategy.Red => LsbChannel.Red, LsbChannelStrategy.Green => LsbChannel.Green, _ => LsbChannel.Blue } };
        for (var y = 0; y < height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < width; x++)
            {
                var nx = horizontal ? x + 1 : x;
                var ny = horizontal ? y : y + 1;
                if (nx >= width || ny >= height) continue;
                var firstPixel = (y * width) + x;
                var secondPixel = (ny * width) + nx;
                foreach (var channel in channels)
                {
                    var firstLogical = layout.TryGetLogicalIndex(firstPixel, channel, recipe.Channels);
                    var secondLogical = layout.TryGetLogicalIndex(secondPixel, channel, recipe.Channels);
                    if (firstLogical is null || secondLogical is null || !selector.Contains(firstLogical.Value) || !selector.Contains(secondLogical.Value)) continue;
                    var first = (bytes[(firstPixel * 4) + (int)channel] >> recipe.BitPlane) & 1;
                    var second = (bytes[(secondPixel * 4) + (int)channel] >> recipe.BitPlane) & 1;
                    if (first == 0 && second == 0) c00++;
                    else if (first == 0) c01++;
                    else if (second == 0) c10++;
                    else c11++;
                }
            }
        }
        return new(c00, c01, c10, c11);
    }

    private static double BinaryEntropy(double p)
    {
        if (p is <= 0 or >= 1) return 0;
        return -(p * Math.Log2(p)) - ((1 - p) * Math.Log2(1 - p));
    }

    private sealed class ScopeSelector
    {
        private readonly LsbStatisticsScope _scope;
        private readonly HashSet<int>? _selected;
        private readonly int _prefixExclusive;

        public ScopeSelector(int eligibleSlots, IReadOnlyList<int> selected, LsbStatisticsScope scope)
        {
            _scope = scope;
            if (selected.Any(value => value < 0 || value >= eligibleSlots)) throw new ArgumentOutOfRangeException(nameof(selected));
            _selected = scope == LsbStatisticsScope.SelectedSlots ? selected.ToHashSet() : null;
            _prefixExclusive = selected.Count == 0 ? 0 : checked(selected.Max() + 1);
        }

        public bool Contains(int logical) => _scope switch
        {
            LsbStatisticsScope.EligibleImage => true,
            LsbStatisticsScope.SelectedSlots => _selected!.Contains(logical),
            _ => logical < _prefixExclusive
        };
    }
}

/// <summary>正规化上不完全 Gamma Q(a,x)，用于把 PoV χ² 转换为 p 值。</summary>
/// <remarks>
/// x&lt;a+1 使用小参数级数求 P 后取 1-P；否则使用 Lentz 连分式直接求 Q。两条路径均设迭代上限、
/// 收敛容差和极小分母保护，非有限输入不会被伪装为合法数值。
/// </remarks>
internal static class RegularizedGamma
{
    private const int MaximumIterations = 10_000;
    private const double Epsilon = 1e-14;
    private const double Tiny = 1e-300;

    public static double Upper(double a, double x)
    {
        if (!double.IsFinite(a) || !double.IsFinite(x) || a <= 0 || x < 0) throw new ArgumentOutOfRangeException(nameof(a), "Gamma Q 要求有限 a>0、x>=0。");
        if (x == 0) return 1;
        if (x < a + 1)
        {
            var sum = 1d / a;
            var term = sum;
            var ap = a;
            for (var iteration = 1; iteration <= MaximumIterations; iteration++)
            {
                ap += 1;
                term *= x / ap;
                sum += term;
                if (Math.Abs(term) <= Math.Abs(sum) * Epsilon)
                    return Math.Clamp(1d - (sum * Math.Exp(-x + (a * Math.Log(x)) - LogGamma(a))), 0d, 1d);
            }
        }
        else
        {
            var b = x + 1 - a;
            var c = 1d / Tiny;
            var d = 1d / Math.Max(Math.Abs(b), Tiny) * Math.Sign(b == 0 ? 1 : b);
            var h = d;
            for (var iteration = 1; iteration <= MaximumIterations; iteration++)
            {
                var an = -iteration * (iteration - a);
                b += 2;
                d = (an * d) + b;
                if (Math.Abs(d) < Tiny) d = Tiny;
                c = b + (an / c);
                if (Math.Abs(c) < Tiny) c = Tiny;
                d = 1d / d;
                var delta = d * c;
                h *= delta;
                if (Math.Abs(delta - 1d) <= Epsilon)
                    return Math.Clamp(Math.Exp(-x + (a * Math.Log(x)) - LogGamma(a)) * h, 0d, 1d);
            }
        }
        throw new ArithmeticException("Gamma Q 在迭代上限内未收敛。");
    }

    private static double LogGamma(double value)
    {
        double[] coefficients = [676.5203681218851, -1259.1392167224028, 771.32342877765313,
            -176.61502916214059, 12.507343278686905, -0.13857109526572012,
            9.9843695780195716e-6, 1.5056327351493116e-7];
        if (value < 0.5) return Math.Log(Math.PI) - Math.Log(Math.Sin(Math.PI * value)) - LogGamma(1 - value);
        value -= 1;
        var sum = 0.99999999999980993;
        for (var index = 0; index < coefficients.Length; index++) sum += coefficients[index] / (value + index + 1);
        var t = value + coefficients.Length - 0.5;
        return 0.5 * Math.Log(2 * Math.PI) + ((value + 0.5) * Math.Log(t)) - t + Math.Log(sum);
    }
}
