using System.Globalization;
using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Domain.ColorTransfer;

/// <summary>确定性 Alpha 加权 Lab k-means；不使用随机种子或运行时算法发现。</summary>
internal sealed class DominantColorClusterer(SrgbGamutMapper gamutMapper, CieDeltaE deltaE)
{
    public const int MaximumIterations = 64;
    public const double ConvergenceDeltaE = 0.05d;

    public ExtractedPalette Cluster(IReadOnlyList<AggregatedColor> colors, int requestedColorCount,
        PaletteSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(colors);
        if (requestedColorCount is < 2 or > 12) throw new ArgumentOutOfRangeException(nameof(requestedColorCount));
        if (colors.Count == 0) throw new ArgumentException("聚类输入不能为空。", nameof(colors));
        var count = Math.Min(requestedColorCount, colors.Count);
        IReadOnlyList<CieLabColor> centers = Initialize(colors, count);
        var assignments = Enumerable.Repeat(-1, colors.Count).ToArray();
        var converged = false; var iterations = 0;
        for (; iterations < MaximumIterations; iterations++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var changed = false;
            for (var i = 0; i < colors.Count; i++)
            {
                var selected = Nearest(colors[i].Lab, centers); if (assignments[i] != selected) { assignments[i] = selected; changed = true; }
            }
            var next = new CieLabColor[count]; var weights = new double[count];
            for (var i = 0; i < colors.Count; i++)
            {
                var cluster = assignments[i]; var weight = colors[i].Weight; weights[cluster] += weight;
                next[cluster] = new CieLabColor(next[cluster].L + (colors[i].Lab.L * weight),
                    next[cluster].A + (colors[i].Lab.A * weight), next[cluster].B + (colors[i].Lab.B * weight));
            }
            var maxMovement = 0d;
            for (var cluster = 0; cluster < count; cluster++)
            {
                if (weights[cluster] <= 0d)
                {
                    var seed = FarthestWeighted(colors, centers); next[cluster] = colors[seed].Lab;
                }
                else next[cluster] = new CieLabColor(next[cluster].L / weights[cluster], next[cluster].A / weights[cluster], next[cluster].B / weights[cluster]);
                maxMovement = Math.Max(maxMovement, deltaE.DeltaE76(centers[cluster], next[cluster]));
            }
            centers = next;
            if (!changed || maxMovement < ConvergenceDeltaE) { converged = true; iterations++; break; }
        }
        // 用最终中心重新分配，保证诊断与返回中心来自同一代。
        for (var i = 0; i < colors.Count; i++) assignments[i] = Nearest(colors[i].Lab, centers);
        var totalWeight = colors.Sum(item => item.Weight); var entries = new List<PaletteEntry>(count);
        for (var cluster = 0; cluster < count; cluster++)
        {
            var weight = 0d; var error = 0d; var max = 0d;
            for (var i = 0; i < colors.Count; i++)
            {
                if (assignments[i] != cluster) continue;
                var distance = deltaE.DeltaE76(colors[i].Lab, centers[cluster]);
                weight += colors[i].Weight; error += distance * colors[i].Weight; max = Math.Max(max, distance);
            }
            var mapped = gamutMapper.Map(centers[cluster]);
            entries.Add(new PaletteEntry(cluster, mapped.Color, centers[cluster], weight, weight / totalWeight,
                weight == 0d ? 0d : error / weight, max));
        }
        var fingerprint = Fingerprint(entries);
        return new ExtractedPalette(requestedColorCount, iterations, converged, fingerprint,
            Array.AsReadOnly(entries.ToArray()), totalWeight, source);
    }

    private List<CieLabColor> Initialize(IReadOnlyList<AggregatedColor> colors, int count)
    {
        var centers = new List<CieLabColor>(count);
        var first = Enumerable.Range(0, colors.Count).OrderByDescending(i => colors[i].Weight).ThenBy(i => colors[i].CellIndex).First();
        centers.Add(colors[first].Lab);
        while (centers.Count < count) centers.Add(colors[FarthestWeighted(colors, centers)].Lab);
        return centers;
    }

    private int FarthestWeighted(IReadOnlyList<AggregatedColor> colors, IReadOnlyList<CieLabColor> centers)
    {
        var selected = 0; var best = double.NegativeInfinity;
        for (var i = 0; i < colors.Count; i++)
        {
            var nearest = centers.Min(center => Squared(colors[i].Lab, center));
            var score = colors[i].Weight * nearest;
            if (score > best || (score == best && colors[i].CellIndex < colors[selected].CellIndex)) { best = score; selected = i; }
        }
        return selected;
    }

    private static int Nearest(CieLabColor color, IReadOnlyList<CieLabColor> centers)
    {
        var selected = 0; var best = Squared(color, centers[0]);
        for (var i = 1; i < centers.Count; i++) { var value = Squared(color, centers[i]); if (value < best) { best = value; selected = i; } }
        return selected;
    }

    private static double Squared(CieLabColor left, CieLabColor right)
    { var l = left.L - right.L; var a = left.A - right.A; var b = left.B - right.B; return (l * l) + (a * a) + (b * b); }

    internal static string Fingerprint(IEnumerable<PaletteEntry> entries)
    {
        // FNV-1a 仅作为内容身份，不作安全摘要；固定不变量格式保证跨区域设置和配置稳定。
        ulong hash = 14695981039346656037UL;
        foreach (var entry in entries.OrderBy(item => item.ClusterIndex))
        {
            var text = string.Create(CultureInfo.InvariantCulture, $"{entry.ClusterIndex}:{entry.Srgb.Red:R}:{entry.Srgb.Green:R}:{entry.Srgb.Blue:R}:{entry.Weight:R};");
            foreach (var character in text) { hash ^= character; hash *= 1099511628211UL; }
        }
        return hash.ToString("x16", CultureInfo.InvariantCulture);
    }
}

/// <summary>纯显示排序服务；返回新列表，不改变 cluster identity 或 fingerprint。</summary>
internal sealed class PaletteSorter(HsvColorSpace hsv)
{
    public IReadOnlyList<PaletteEntry> Sort(IReadOnlyList<PaletteEntry> entries, PaletteSort sort) => sort switch
    {
        PaletteSort.Proportion => entries.OrderByDescending(item => item.Proportion).ThenBy(item => item.ClusterIndex).ToArray(),
        PaletteSort.Lightness => entries.OrderBy(item => item.Lab.L).ThenBy(item => item.ClusterIndex).ToArray(),
        PaletteSort.Hue => entries.OrderBy(item => HueKey(item.Srgb)).ThenBy(item => item.ClusterIndex).ToArray(),
        _ => throw new ArgumentOutOfRangeException(nameof(sort))
    };

    private double HueKey(SrgbColor color)
    { var value = hsv.ToHsv(color); return value.HueStatus == HueStatus.Defined ? value.HueDegrees : 360d; }
}
