using ImageLabPlugin.Domain.Comparison;
using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Domain.ColorTransfer;

/// <summary>按 ΔE76 精确选择冻结调色板最近色，并以 ΔE00 报告感知误差。</summary>
/// <remarks>
/// V1 的 K 最大为 12，因此逐像素精确比较 K 次可保持语义透明，无需引入未披露误差的 3D LUT。
/// 并列始终取较小 ClusterIndex；显示排序不会影响输出。Alpha 原字节保留，全透明 RGBA 完全不变。
/// </remarks>
internal sealed class FixedPaletteRemapper(
    SrgbColorSpace srgb,
    CieLabColorSpace lab,
    ColorDistributionAnalyzer distributions,
    PerceptualDifferenceAnalyzer differences,
    FullReferenceQualityAnalyzer quality)
{
    public ColorOperationResult Remap(PixelImage target, FrozenPalette palette, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(palette);
        if (palette.Entries.Count is < 2 or > 12) throw new ArgumentException("冻结调色板必须包含 2–12 色。", nameof(palette));
        var stable = palette.Entries.OrderBy(entry => entry.ClusterIndex).ToArray();
        var counts = new long[stable.Length]; var weights = new double[stable.Length];
        var result = target.Clone(); var source = target.Rgba.Span; var output = result.WritableRgba;
        // 行优先扫描且每行检查取消；临时状态仅为 K 项计数，取消不会修改输入或提交半结果。
        for (var y = 0; y < target.Size.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < target.Size.Width; x++)
            {
                var offset = ((y * target.Size.Width) + x) * 4; var alpha = source[offset + 3]; if (alpha == 0) continue;
                var color = lab.ToLab(srgb.ToXyz(srgb.Decode(SrgbColor.FromBytes(source[offset], source[offset + 1], source[offset + 2]))));
                var selected = 0; var best = Squared(color, stable[0].Lab);
                for (var i = 1; i < stable.Length; i++) { var candidate = Squared(color, stable[i].Lab); if (candidate < best) { best = candidate; selected = i; } }
                var bytes = stable[selected].Srgb.ToBytes(); output[offset] = bytes.Red; output[offset + 1] = bytes.Green; output[offset + 2] = bytes.Blue;
                counts[selected]++; weights[selected] += alpha / 255d;
            }
        }
        var distribution = distributions.Analyze(result, cancellationToken);
        var difference = differences.Analyze(target, result, cancellationToken);
        var metrics = quality.Analyze(target, result, cancellationToken);
        return new ColorOperationResult(ColorOperationKind.FixedPaletteRemap, result,
            new GamutMappingDiagnostics(target.Size.PixelCount, 0, 0, 0d), difference,
            $"remap:{palette.Fingerprint}:{SrgbColorSpace.ProtocolId}", distribution,
            Array.AsReadOnly(counts), Array.AsReadOnly(weights), metrics, null, null);
    }

    private static double Squared(CieLabColor left, CieLabColor right)
    { var l = left.L - right.L; var a = left.A - right.A; var b = left.B - right.B; return (l * l) + (a * a) + (b * b); }
}
