using System.Globalization;
using ImageLabPlugin.Domain.Shared.Analysis;
using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Domain.ColorTransfer;

/// <summary>执行 CIELAB 独立通道均值/标准差迁移。</summary>
/// <remarks>
/// 参考图只提供全局统计，因此目标和参考可以不同尺寸；本算法不是像素配对，也不缩放或对齐参考图。
/// 目标零方差时通道收敛到参考均值。每次只读取原目标并生成新 PixelImage，取消时不会暴露半结果。
/// </remarks>
internal sealed class LabStatisticsTransfer(
    SrgbColorSpace srgb,
    CieLabColorSpace lab,
    SrgbGamutMapper gamut,
    ColorDistributionAnalyzer distributions,
    PerceptualDifferenceAnalyzer differences,
    FullReferenceQualityAnalyzer quality)
{
    private const double VarianceEpsilon = 1e-9;

    public ColorOperationResult Transfer(PixelImage target, ColorDistributionSnapshot targetDistribution,
        ColorDistributionSnapshot referenceDistribution, ColorTransferRecipe recipe, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(targetDistribution);
        ArgumentNullException.ThrowIfNull(referenceDistribution); recipe.Validate();
        PixelImage result;
        long unchanged = 0, compressed = 0, clipped = 0; double maxMapping = 0d;
        if (recipe.Strength == 0d)
        {
            result = target.Clone(); unchanged = target.Size.PixelCount;
        }
        else
        {
            result = target.Clone(); var source = target.Rgba.Span; var output = result.WritableRgba;
            var targetMean = targetDistribution.Statistics.MeanLab;
            var targetStd = targetDistribution.Statistics.StandardDeviationLab;
            var referenceMean = referenceDistribution.Statistics.MeanLab;
            var referenceStd = referenceDistribution.Statistics.StandardDeviationLab;
            // 行优先扫描让取消粒度和内存峰值可预测；A=0 的四个字节保持 clone 原值。
            for (var y = 0; y < target.Size.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (var x = 0; x < target.Size.Width; x++)
                {
                    var offset = ((y * target.Size.Width) + x) * 4; if (source[offset + 3] == 0) { unchanged++; continue; }
                    var original = ToLab(source[offset], source[offset + 1], source[offset + 2]);
                    var mappedL = recipe.Mode == ColorTransferMode.PreserveTargetLightness ? original.L :
                        Mix(original.L, Map(original.L, targetMean.L, targetStd.L, referenceMean.L, referenceStd.L), recipe.Strength);
                    var mapped = new CieLabColor(mappedL,
                        Mix(original.A, Map(original.A, targetMean.A, targetStd.A, referenceMean.A, referenceStd.A), recipe.Strength),
                        Mix(original.B, Map(original.B, targetMean.B, targetStd.B, referenceMean.B, referenceStd.B), recipe.Strength));
                    var encoded = gamut.Map(mapped); var bytes = encoded.Color.ToBytes();
                    output[offset] = bytes.Red; output[offset + 1] = bytes.Green; output[offset + 2] = bytes.Blue;
                    if (encoded.Kind == GamutMappingKind.None) unchanged++;
                    if ((encoded.Kind & GamutMappingKind.ChromaCompressed) != 0) compressed++;
                    if ((encoded.Kind & GamutMappingKind.LightnessClipped) != 0) clipped++;
                    maxMapping = Math.Max(maxMapping, encoded.DeltaE76);
                }
            }
        }
        var distribution = distributions.Analyze(result, cancellationToken);
        var difference = differences.Analyze(target, result, cancellationToken);
        var metrics = quality.Analyze(target, result, cancellationToken);
        var fingerprint = string.Create(CultureInfo.InvariantCulture,
            $"transfer:{SrgbColorSpace.ProtocolId}:{recipe.Mode}:{recipe.Strength:R}:{target.Size.Width}x{target.Size.Height}");
        return new ColorOperationResult(ColorOperationKind.StatisticsTransfer, result,
            new GamutMappingDiagnostics(unchanged, compressed, clipped, maxMapping), difference, fingerprint,
            distribution, Array.Empty<long>(), Array.Empty<double>(), metrics,
            Compare(targetDistribution, referenceDistribution), Compare(distribution, referenceDistribution));
    }

    private CieLabColor ToLab(byte r, byte g, byte b) => lab.ToLab(srgb.ToXyz(srgb.Decode(SrgbColor.FromBytes(r, g, b))));
    private static double Map(double value, double targetMean, double targetStd, double referenceMean, double referenceStd) =>
        targetStd < VarianceEpsilon ? referenceMean : referenceMean + ((value - targetMean) * referenceStd / targetStd);
    private static double Mix(double original, double mapped, double strength) => original + (strength * (mapped - original));

    private static DistributionCloseness Compare(ColorDistributionSnapshot candidate, ColorDistributionSnapshot reference)
    {
        var mean = candidate.Statistics.MeanLab; var referenceMean = reference.Statistics.MeanLab;
        var standard = candidate.Statistics.StandardDeviationLab; var referenceStandard = reference.Statistics.StandardDeviationLab;
        return new DistributionCloseness(
            Norm(mean.L - referenceMean.L, mean.A - referenceMean.A, mean.B - referenceMean.B),
            Norm(standard.L - referenceStandard.L, standard.A - referenceStandard.A, standard.B - referenceStandard.B),
            ColorDistributionAnalyzer.JensenShannonDistance(candidate.LabHistogram.Take(100).ToArray(), reference.LabHistogram.Take(100).ToArray()),
            ColorDistributionAnalyzer.JensenShannonDistance(candidate.LabHistogram.Skip(100).Take(256).ToArray(), reference.LabHistogram.Skip(100).Take(256).ToArray()),
            ColorDistributionAnalyzer.JensenShannonDistance(candidate.LabHistogram.Skip(356).Take(256).ToArray(), reference.LabHistogram.Skip(356).Take(256).ToArray()));
    }

    private static double Norm(double first, double second, double third) => Math.Sqrt((first * first) + (second * second) + (third * third));
}
