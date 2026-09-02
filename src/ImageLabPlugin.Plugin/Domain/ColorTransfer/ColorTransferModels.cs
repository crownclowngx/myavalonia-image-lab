using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Domain.Shared.Analysis;

namespace ImageLabPlugin.Domain.ColorTransfer;

internal static class ColorTransferProtocols
{
    public const string Alpha = "straight-alpha-weight-a-over-255-v1";
    public const string Clustering = "rgb5-weighted-lab-kmeans-v1";
    public const string GamutMapping = "lab-preserve-l-hue-chroma-bisection-v1";
    public const string ReportSchema = "image-lab.palette-color-transfer-report/1";
}

internal enum PaletteSource { Target, Reference }
internal enum PaletteSort { Proportion, Lightness, Hue }
internal enum ColorTransferMode { FullLab, PreserveTargetLightness }
internal enum ColorOperationKind { StatisticsTransfer, FixedPaletteRemap }
[Flags]
internal enum GamutMappingKind { None = 0, ChromaCompressed = 1, LightnessClipped = 2 }

/// <summary>一个通道的 Alpha 加权均值、总体标准差和直方图近似分位数。</summary>
internal sealed record ChannelStatistics(double Mean, double StandardDeviation, double P05, double P50, double P95);

/// <summary>完整扫描得到的颜色事实；有效像素数与有效 Alpha 权重分开报告。</summary>
internal sealed record ColorStatistics(
    long PixelCount,
    long VisiblePixelCount,
    double EffectiveWeight,
    SrgbColor MeanRgb,
    CieLabColor MeanLab,
    CieLabColor StandardDeviationLab,
    ChannelStatistics Lightness,
    ChannelStatistics LabA,
    ChannelStatistics LabB,
    double? CircularMeanHueDegrees,
    double HueConcentration,
    double DefinedHueWeight,
    double UndefinedHueWeight);

/// <summary>
/// 一维和二维分布的拥有值。数组在构造时复制，避免 Document 或控件修改领域事实。
/// </summary>
internal sealed class ColorDistributionSnapshot
{
    public ColorDistributionSnapshot(ColorStatistics statistics, double[] rgb, double[] hsv, double[] lab,
        double[] hueSaturation, double[] labAb, double labAUnderflow, double labAOverflow,
        double labBUnderflow, double labBOverflow)
    {
        Statistics = statistics;
        RgbHistogram = Array.AsReadOnly((double[])rgb.Clone());
        HsvHistogram = Array.AsReadOnly((double[])hsv.Clone());
        LabHistogram = Array.AsReadOnly((double[])lab.Clone());
        HueSaturationDensity = Array.AsReadOnly((double[])hueSaturation.Clone());
        LabAbDensity = Array.AsReadOnly((double[])labAb.Clone());
        LabAUnderflow = labAUnderflow; LabAOverflow = labAOverflow;
        LabBUnderflow = labBUnderflow; LabBOverflow = labBOverflow;
    }

    public ColorStatistics Statistics { get; }
    public IReadOnlyList<double> RgbHistogram { get; }
    public IReadOnlyList<double> HsvHistogram { get; }
    public IReadOnlyList<double> LabHistogram { get; }
    public IReadOnlyList<double> HueSaturationDensity { get; }
    public IReadOnlyList<double> LabAbDensity { get; }
    public double LabAUnderflow { get; }
    public double LabAOverflow { get; }
    public double LabBUnderflow { get; }
    public double LabBOverflow { get; }
}

/// <summary>聚类项使用稳定 ClusterIndex 作为身份；显示排序不改变此索引。</summary>
internal sealed record PaletteEntry(int ClusterIndex, SrgbColor Srgb, CieLabColor Lab,
    double Weight, double Proportion, double MeanDeltaE76, double MaximumDeltaE76);

internal sealed record ExtractedPalette(int RequestedColorCount, int Iterations, bool Converged,
    string Fingerprint, IReadOnlyList<PaletteEntry> Entries, double EffectiveWeight, PaletteSource Source);

internal sealed record FrozenPalette(string SourceFingerprint, IReadOnlyList<PaletteEntry> Entries,
    PaletteSource Source, string Fingerprint);

internal sealed record ColorTransferRecipe(ColorTransferMode Mode, double Strength)
{
    public ColorTransferRecipe Validate()
    {
        if (!double.IsFinite(Strength) || Strength is < 0d or > 1d)
            throw new ArgumentOutOfRangeException(nameof(Strength), "迁移强度必须位于 [0,1]。");
        return this;
    }
}

internal sealed record GamutMappingDiagnostics(long UnchangedCount, long ChromaCompressedCount,
    long LightnessClippedCount, double MaximumDeltaE76);

internal sealed record DifferenceSummary(double Mean, double P50, double P95, double Maximum,
    long ChangedPixelCount, IReadOnlyList<double> Histogram);

internal sealed record DistributionCloseness(double MeanResidual, double StandardDeviationResidual,
    double JensenShannonL, double JensenShannonA, double JensenShannonB);

internal sealed record ColorOperationResult(ColorOperationKind Kind, PixelImage Image,
    GamutMappingDiagnostics Gamut, DifferenceSummary Difference, string RecipeFingerprint,
    ColorDistributionSnapshot Distribution, IReadOnlyList<long> PalettePixelCounts,
    IReadOnlyList<double> PaletteWeights, FullReferenceQualityMetrics Quality,
    DistributionCloseness? BeforeReferenceCloseness, DistributionCloseness? AfterReferenceCloseness);

internal sealed record GamutMappedColor(SrgbColor Color, CieLabColor MappedLab,
    GamutMappingKind Kind, double DeltaE76);
