using System.Numerics;
using ImageLabPlugin.Domain.Shared.Spectral;

namespace ImageLabPlugin.Domain.MagnitudePhaseSwap;

internal enum MagnitudePhaseMetricStatus { Available, ExactMatch, NotApplicable, Undefined }

/// <summary>不使用 NaN/Infinity 冒充有效值的结构化指标。</summary>
internal sealed record MagnitudePhaseMetric(MagnitudePhaseMetricStatus Status, double? Value, string Unit, string? Reason)
{
    public static MagnitudePhaseMetric Available(double value, string unit) =>
        double.IsFinite(value) ? new(MagnitudePhaseMetricStatus.Available, value, unit, null) :
            new(MagnitudePhaseMetricStatus.Undefined, null, unit, "数值不可定义。");
    public static MagnitudePhaseMetric NotApplicable(string unit, string reason) =>
        new(MagnitudePhaseMetricStatus.NotApplicable, null, unit, reason);
    public static MagnitudePhaseMetric Exact(string unit) =>
        new(MagnitudePhaseMetricStatus.ExactMatch, null, unit, "均方误差为 0；PSNR 理论为正无穷，协议以 ExactMatch 表达而不输出 Infinity。");
    public static MagnitudePhaseMetric Undefined(string unit, string reason) =>
        new(MagnitudePhaseMetricStatus.Undefined, null, unit, reason);
}

internal sealed record MagnitudePhaseSpatialDiagnostics(
    MagnitudePhaseMetric NccA, MagnitudePhaseMetric NccB,
    MagnitudePhaseMetric GradientCorrelationA, MagnitudePhaseMetric GradientCorrelationB,
    MagnitudePhaseMetric PsnrA, MagnitudePhaseMetric PsnrB,
    MagnitudePhaseMetric SsimA, MagnitudePhaseMetric SsimB);

internal sealed record MagnitudePhaseEnergyDiagnostics(double DcMagnitude, double SpectrumEnergy,
    double SpatialEnergy, double ParsevalRelativeError);

internal sealed record MagnitudePhaseDiagnosticsResult(SpectrumMixDiagnostics Mix,
    MagnitudePhaseSpatialDiagnostics Spatial, MagnitudePhaseEnergyDiagnostics SourceA,
    MagnitudePhaseEnergyDiagnostics SourceB, MagnitudePhaseEnergyDiagnostics Result,
    MagnitudePhaseProjectionStatistics Projection, double MaximumImaginaryResidual,
    double RelativeImaginaryResidual);

/// <summary>计算频域守恒事实与空间相似性，不输出“获胜图片”或因果百分比。</summary>
/// <remarks>
/// NCC 使用去均值亮度，梯度相关使用固定中心差分。PSNR-Y 和全局 SSIM-Y 只对物理投影有量纲意义；
/// 科学投影是为观察正负振荡而重标度的诊断图，因此固定返回结构化 N/A，不能让 0 或 NaN 冒充质量分数。
/// Parseval 按本项目未归一化正变换约定比较 <c>Σ|F|²/N²</c> 与 <c>Σx²</c>。
/// </remarks>
internal sealed class MagnitudePhaseDiagnostics
{
    public MagnitudePhaseDiagnosticsResult Analyze(FrequencyPairCanvas a, FrequencyPairCanvas b,
        FrequencySpectrum spectrumA, FrequencySpectrum spectrumB, MagnitudePhaseEnergyDiagnostics resultEnergy,
        MagnitudePhaseRawResult raw, MagnitudePhaseProjectionResult projection, SpectrumMixDiagnostics mix,
        CancellationToken cancellationToken = default)
    {
        var spatial = new MagnitudePhaseSpatialDiagnostics(
            Correlation(raw.Values.Span, a.Values.Span), Correlation(raw.Values.Span, b.Values.Span),
            GradientCorrelation(raw.Values.Span, a.Values.Span, raw.Size, cancellationToken),
            GradientCorrelation(raw.Values.Span, b.Values.Span, raw.Size, cancellationToken),
            projection.Kind == MagnitudePhaseProjectionKind.PhysicalClamp ? Psnr(projection.Image, a.Values.Span) : ScientificNa("dB"),
            projection.Kind == MagnitudePhaseProjectionKind.PhysicalClamp ? Psnr(projection.Image, b.Values.Span) : ScientificNa("dB"),
            projection.Kind == MagnitudePhaseProjectionKind.PhysicalClamp ? Ssim(projection.Image, a.Values.Span) : ScientificNa("ratio"),
            projection.Kind == MagnitudePhaseProjectionKind.PhysicalClamp ? Ssim(projection.Image, b.Values.Span) : ScientificNa("ratio"));
        return new MagnitudePhaseDiagnosticsResult(mix, spatial,
            Energy(spectrumA.Values.Span, a.Values.Span), Energy(spectrumB.Values.Span, b.Values.Span),
            resultEnergy, projection.Statistics,
            raw.MaximumImaginaryResidual, raw.RelativeImaginaryResidual);
    }

    private static MagnitudePhaseMetric ScientificNa(string unit) =>
        MagnitudePhaseMetric.NotApplicable(unit, "科学投影不保留原亮度量纲，PSNR/SSIM 不适用。");

    private static MagnitudePhaseMetric Correlation(ReadOnlySpan<double> first, ReadOnlySpan<double> second)
    {
        var meanA = Mean(first); var meanB = Mean(second);
        double covariance = 0d, normA = 0d, normB = 0d;
        for (var i = 0; i < first.Length; i++)
        {
            var a = first[i] - meanA; var b = second[i] - meanB;
            covariance += a * b; normA += a * a; normB += b * b;
        }
        var denominator = Math.Sqrt(normA * normB);
        return denominator <= 1e-30 ? MagnitudePhaseMetric.Undefined("ratio", "至少一张图为常量，NCC 不可定义。") :
            MagnitudePhaseMetric.Available(covariance / denominator, "ratio");
    }

    private static MagnitudePhaseMetric GradientCorrelation(ReadOnlySpan<double> first, ReadOnlySpan<double> second,
        int size, CancellationToken cancellationToken)
    {
        double covariance = 0d, normA = 0d, normB = 0d;
        for (var y = 1; y < size - 1; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 1; x < size - 1; x++)
            {
                var i = (y * size) + x;
                var ax = first[i + 1] - first[i - 1]; var ay = first[i + size] - first[i - size];
                var bx = second[i + 1] - second[i - 1]; var by = second[i + size] - second[i - size];
                covariance += (ax * bx) + (ay * by);
                normA += (ax * ax) + (ay * ay); normB += (bx * bx) + (by * by);
            }
        }
        var denominator = Math.Sqrt(normA * normB);
        return denominator <= 1e-30 ? MagnitudePhaseMetric.Undefined("ratio", "至少一张图没有可测梯度。") :
            MagnitudePhaseMetric.Available(covariance / denominator, "ratio");
    }

    private static MagnitudePhaseMetric Psnr(ImageLabPlugin.Domain.Shared.Imaging.PixelImage image, ReadOnlySpan<double> reference)
    {
        double mse = 0d;
        for (var i = 0; i < reference.Length; i++)
        {
            var x = i % image.Size.Width; var y = i / image.Size.Width;
            var delta = image.GetPixel(x, y).R - reference[i]; mse += delta * delta;
        }
        mse /= reference.Length;
        if (mse <= 1e-30) return MagnitudePhaseMetric.Exact("dB");
        return MagnitudePhaseMetric.Available(10d * Math.Log10((255d * 255d) / mse), "dB");
    }

    private static MagnitudePhaseMetric Ssim(ImageLabPlugin.Domain.Shared.Imaging.PixelImage image, ReadOnlySpan<double> reference)
    {
        var count = reference.Length;
        double meanA = 0d, meanB = Mean(reference);
        for (var i = 0; i < count; i++) meanA += image.Rgba.Span[i * 4];
        meanA /= count;
        double varianceA = 0d, varianceB = 0d, covariance = 0d;
        for (var i = 0; i < count; i++)
        {
            var a = image.Rgba.Span[i * 4] - meanA; var b = reference[i] - meanB;
            varianceA += a * a; varianceB += b * b; covariance += a * b;
        }
        var divisor = Math.Max(1, count - 1);
        varianceA /= divisor; varianceB /= divisor; covariance /= divisor;
        var c1 = Math.Pow(.01d * 255d, 2d); var c2 = Math.Pow(.03d * 255d, 2d);
        var value = ((2d * meanA * meanB + c1) * (2d * covariance + c2)) /
                    ((meanA * meanA + meanB * meanB + c1) * (varianceA + varianceB + c2));
        return MagnitudePhaseMetric.Available(value, "ratio");
    }

    internal static MagnitudePhaseEnergyDiagnostics Energy(ReadOnlySpan<Complex> spectrum, ReadOnlySpan<double> spatial)
    {
        double spectrumEnergy = 0d, spatialEnergy = 0d;
        foreach (var value in spectrum) spectrumEnergy += value.Magnitude * value.Magnitude;
        foreach (var value in spatial) spatialEnergy += value * value;
        var normalized = spectrumEnergy / spectrum.Length;
        return new MagnitudePhaseEnergyDiagnostics(spectrum[0].Magnitude, spectrumEnergy, spatialEnergy,
            Math.Abs(normalized - spatialEnergy) / Math.Max(1e-30, spatialEnergy));
    }

    private static double Mean(ReadOnlySpan<double> values)
    {
        double sum = 0d; foreach (var value in values) sum += value; return sum / values.Length;
    }
}
