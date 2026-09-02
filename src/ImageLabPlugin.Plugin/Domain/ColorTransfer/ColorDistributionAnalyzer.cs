using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Domain.ColorTransfer;

/// <summary>以固定数组完成 Alpha 加权颜色统计、直方图和二维密度。</summary>
/// <remarks>
/// 热点扫描按行优先，每行检查取消；A=0 完全跳过，0&lt;A&lt;255 以 A/255 加权。
/// 只分配固定 3×256、380、356、180×100 和 128×128 数组，不建立逐像素 Lab 对象集合。
/// </remarks>
internal sealed class ColorDistributionAnalyzer(SrgbColorSpace srgb, CieLabColorSpace lab, HsvColorSpace hsv)
{
    public ColorDistributionSnapshot Analyze(PixelImage image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        var rgbHist = new double[3 * 256];
        var hsvHist = new double[180 + 100 + 100];
        var labHist = new double[100 + 256 + 256];
        var hs = new double[180 * 100]; var ab = new double[128 * 128];
        var r = new WeightedMoment(); var g = new WeightedMoment(); var b = new WeightedMoment();
        var lMoment = new WeightedMoment(); var aMoment = new WeightedMoment(); var bMoment = new WeightedMoment();
        long visible = 0; double hueX = 0d, hueY = 0d, hueWeight = 0d, noHueWeight = 0d;
        double aUnder = 0d, aOver = 0d, bUnder = 0d, bOver = 0d;
        var pixels = image.Rgba.Span;
        for (var y = 0; y < image.Size.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < image.Size.Width; x++)
            {
                var offset = ((y * image.Size.Width) + x) * 4;
                var alpha = pixels[offset + 3]; if (alpha == 0) continue;
                visible++; var weight = alpha / 255d;
                var color = SrgbColor.FromBytes(pixels[offset], pixels[offset + 1], pixels[offset + 2]);
                var labColor = lab.ToLab(srgb.ToXyz(srgb.Decode(color))); var hsvColor = hsv.ToHsv(color);
                r.Add(color.Red, weight); g.Add(color.Green, weight); b.Add(color.Blue, weight);
                lMoment.Add(labColor.L, weight); aMoment.Add(labColor.A, weight); bMoment.Add(labColor.B, weight);
                rgbHist[pixels[offset]] += weight; rgbHist[256 + pixels[offset + 1]] += weight;
                rgbHist[512 + pixels[offset + 2]] += weight;
                var sBin = UnitBin(hsvColor.Saturation, 100); var vBin = UnitBin(hsvColor.Value, 100);
                hsvHist[180 + sBin] += weight; hsvHist[280 + vBin] += weight;
                if (hsvColor.HueStatus == HueStatus.Defined)
                {
                    var hBin = Math.Min(179, (int)(hsvColor.HueDegrees / 2d));
                    hsvHist[hBin] += weight; hs[(hBin * 100) + sBin] += weight;
                    var radians = hsvColor.HueDegrees * Math.PI / 180d;
                    hueX += weight * Math.Cos(radians); hueY += weight * Math.Sin(radians); hueWeight += weight;
                }
                else noHueWeight += weight;
                labHist[Math.Clamp((int)labColor.L, 0, 99)] += weight;
                AddLabBin(labColor.A, weight, labHist, 100, ref aUnder, ref aOver);
                AddLabBin(labColor.B, weight, labHist, 356, ref bUnder, ref bOver);
                if (labColor.A < -128d) aUnder += 0d; else if (labColor.A >= 128d) aOver += 0d;
                if (labColor.B < -128d) bUnder += 0d; else if (labColor.B >= 128d) bOver += 0d;
                if (labColor.A is >= -128d and < 128d && labColor.B is >= -128d and < 128d)
                {
                    var ax = Math.Clamp((int)((labColor.A + 128d) / 2d), 0, 127);
                    var by = Math.Clamp((int)((labColor.B + 128d) / 2d), 0, 127);
                    ab[(by * 128) + ax] += weight;
                }
            }
        }
        if (lMoment.Weight < 1e-12) throw new InvalidOperationException("图片没有可参与颜色统计的可见像素。");
        double? hueMean = null; var concentration = 0d;
        if (hueWeight > 0d)
        {
            hueMean = Math.Atan2(hueY, hueX) * 180d / Math.PI; if (hueMean < 0d) hueMean += 360d;
            concentration = Math.Sqrt((hueX * hueX) + (hueY * hueY)) / hueWeight;
        }
        var stats = new ColorStatistics(image.Size.PixelCount, visible, lMoment.Weight,
            new SrgbColor(r.Mean, g.Mean, b.Mean), new CieLabColor(lMoment.Mean, aMoment.Mean, bMoment.Mean),
            new CieLabColor(lMoment.StandardDeviation, aMoment.StandardDeviation, bMoment.StandardDeviation),
            Channel(lMoment, labHist, 0, 100, 0d, 100d), Channel(aMoment, labHist, 100, 256, -128d, 128d),
            Channel(bMoment, labHist, 356, 256, -128d, 128d), hueMean, concentration, hueWeight, noHueWeight);
        return new ColorDistributionSnapshot(stats, rgbHist, hsvHist, labHist, hs, ab, aUnder, aOver, bUnder, bOver);
    }

    public static double JensenShannonDistance(IReadOnlyList<double> first, IReadOnlyList<double> second)
    {
        ArgumentNullException.ThrowIfNull(first); ArgumentNullException.ThrowIfNull(second);
        if (first.Count != second.Count || first.Count == 0) throw new ArgumentException("分布必须具有相同且非零的 bin 数。");
        var sumP = first.Sum(); var sumQ = second.Sum();
        if (sumP <= 0d || sumQ <= 0d) throw new ArgumentException("分布总权重必须大于零。");
        var divergence = 0d;
        for (var i = 0; i < first.Count; i++)
        {
            var p = first[i] / sumP; var q = second[i] / sumQ; var m = (p + q) / 2d;
            if (p > 0d) divergence += 0.5d * p * Math.Log(p / m);
            if (q > 0d) divergence += 0.5d * q * Math.Log(q / m);
        }
        return Math.Sqrt(Math.Max(0d, divergence));
    }

    private static ChannelStatistics Channel(WeightedMoment moment, double[] histogram, int offset, int count, double min, double max) =>
        new(moment.Mean, moment.StandardDeviation, Quantile(histogram, offset, count, min, max, 0.05d),
            Quantile(histogram, offset, count, min, max, 0.5d), Quantile(histogram, offset, count, min, max, 0.95d));

    private static double Quantile(double[] bins, int offset, int count, double min, double max, double q)
    {
        var total = 0d; for (var i = 0; i < count; i++) total += bins[offset + i];
        var target = total * q; var cumulative = 0d;
        for (var i = 0; i < count; i++) { cumulative += bins[offset + i]; if (cumulative >= target) return min + ((i + 0.5d) * (max - min) / count); }
        return max;
    }

    private static void AddLabBin(double value, double weight, double[] bins, int offset, ref double under, ref double over)
    { if (value < -128d) under += weight; else if (value >= 128d) over += weight; else bins[offset + (int)(value + 128d)] += weight; }
    private static int UnitBin(double value, int count) => Math.Min(count - 1, (int)(value * count));

    private sealed class WeightedMoment
    {
        private double _m2;
        public double Weight { get; private set; }
        public double Mean { get; private set; }
        public double StandardDeviation => Weight <= 0d ? 0d : Math.Sqrt(Math.Max(0d, _m2 / Weight));
        public void Add(double value, double weight)
        { var total = Weight + weight; var delta = value - Mean; Mean += (weight / total) * delta; _m2 += weight * delta * (value - Mean); Weight = total; }
    }
}
