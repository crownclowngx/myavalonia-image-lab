using System.Numerics;
using ImageLabPlugin.Domain.Shared.Analysis;
using ImageLabPlugin.Domain.Shared.Spectral;
using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Domain.SpectralArt;

internal sealed record SpectralVisibilityDiagnostics(
    bool IsAvailable,
    string? NotAvailableReason,
    double SourceForegroundLogPower,
    double SourceBackgroundLogPower,
    double ResultForegroundLogPower,
    double ResultBackgroundLogPower,
    double SourceVisibility,
    double ResultVisibility,
    double VisibilityIncrease);

internal sealed record SpectralFrequencyDiagnostics(
    int IndependentWrittenBins,
    int TotalWrittenBins,
    double OccupiedRatio,
    double SourceEnergy,
    double ResultEnergy,
    double EnergyIncreaseRatio,
    double MaximumPhaseDeviation,
    double MaximumConjugateResidual,
    SpectralVisibilityDiagnostics Visibility);

/// <summary>计算 Spectral Art 的质量、可见性和解释性预览，不修改任何输入。</summary>
/// <remarks>
/// 空间质量委托项目统一的 FullReferenceQualityAnalyzer，差异委托现有频域通道差异投影；本服务只增加
/// Spectral Art 特有的前景/留白对数功率、能量、图案映射和真实频谱差异。可见性是同一次实验内的相对量，
/// 不是识别率、扫码成功率或隐写安全证明；前景或背景样本不足时返回明确 N/A，而不是误导性的零。
/// </remarks>
internal sealed class SpectralArtDiagnostics(
    FullReferenceQualityAnalyzer qualityAnalyzer,
    ChannelDifferenceProjector differenceProjector,
    RadialLogPowerBaseline radialBaseline)
{
    /// <summary>强度零在创建完整工作频谱前返回真实源能量和明确的“未写入”诊断。</summary>
    public SpectralFrequencyDiagnostics AnalyzeNoOp(
        FrequencySpectrum source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        double energy = 0d;
        var values = source.Values.Span;
        for (var i = 0; i < values.Length; i++)
        {
            if ((i & 16383) == 0) cancellationToken.ThrowIfCancellationRequested();
            energy += (values[i].Real * values[i].Real) + (values[i].Imaginary * values[i].Imaginary);
        }
        if (!double.IsFinite(energy)) throw new InvalidDataException("源频谱总能量出现非有限值。");
        return new SpectralFrequencyDiagnostics(0, 0, 0d, energy, energy, 0d, 0d, 0d,
            new SpectralVisibilityDiagnostics(false, "强度为 0，未执行幅度写入；可见性增量为 0。",
                0d, 0d, 0d, 0d, 0d, 0d, 0d));
    }

    /// <summary>强度零的频谱差异是确定性全黑图，不创建或比较第二份复数频谱。</summary>
    public static PixelImage CreateZeroSpectrumDifference(FrequencySpectrum source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        var rgba = new byte[checked(source.ValueCount * 4)];
        for (var pixel = 0; pixel < source.ValueCount; pixel++)
        {
            if ((pixel & 16383) == 0) cancellationToken.ThrowIfCancellationRequested();
            rgba[(pixel * 4) + 3] = 255;
        }
        return new PixelImage(new ImageSize(source.PaddedWidth, source.PaddedHeight), rgba);
    }

    public FullReferenceQualityMetrics AnalyzeQuality(
        PixelImage source,
        PixelImage result,
        CancellationToken cancellationToken = default) =>
        qualityAnalyzer.Analyze(source, result, cancellationToken);

    public ChannelDifferenceProjection CreateSpatialDifference(
        ImageChannelPlane source,
        ImageChannelPlane result,
        double amplification,
        CancellationToken cancellationToken = default) =>
        differenceProjector.Project(source, result, amplification, cancellationToken);

    public SpectralFrequencyDiagnostics AnalyzeFrequency(
        FrequencySpectrum source,
        Complex[] written,
        SpectralPatternMapping mapping,
        SpectralAmplitudeWriteStatistics write,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(written);
        ArgumentNullException.ThrowIfNull(mapping);
        if (written.Length != source.ValueCount) throw new ArgumentException("频谱长度不一致。", nameof(written));
        var baseline = radialBaseline.Analyze(source, cancellationToken);
        double sourceForeground = 0d, sourceBackground = 0d;
        double resultForeground = 0d, resultBackground = 0d, localScale = 0d;
        var foregroundCount = 0;
        var backgroundCount = 0;
        var sourceValues = source.Values.Span;
        var weights = mapping.MainWeightSpan;
        for (var localY = 0; localY < mapping.Height; localY++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var localX = 0; localX < mapping.Width; localX++)
            {
                var weight = weights[(localY * mapping.Width) + localX];
                if (weight is > 0.05d and < 0.5d) continue;
                var point = FrequencyCoordinates.FromDisplay(mapping.Left + localX, mapping.Top + localY,
                    source.PaddedWidth, source.PaddedHeight);
                var index = (point.InternalY * source.PaddedWidth) + point.InternalX;
                var before = LogPower(sourceValues[index]);
                var after = LogPower(written[index]);
                var radial = RadialLogPowerBaseline.RadialBin(point.InternalX, point.InternalY,
                    source.PaddedWidth, source.PaddedHeight);
                var scale = Math.Max(SpectralAmplitudeWriter.MinimumRobustScale,
                    RadialLogPowerBaseline.RobustScale * baseline.MedianAbsoluteDeviations[radial]);
                if (weight >= 0.5d)
                {
                    sourceForeground += before;
                    resultForeground += after;
                    localScale += scale;
                    foregroundCount++;
                }
                else
                {
                    sourceBackground += before;
                    resultBackground += after;
                    localScale += scale;
                    backgroundCount++;
                }
            }
        }

        SpectralVisibilityDiagnostics visibility;
        if (foregroundCount == 0 || backgroundCount == 0)
        {
            visibility = new SpectralVisibilityDiagnostics(false,
                foregroundCount == 0 ? "图案没有足够前景样本。" : "图案区域没有足够留白背景样本。",
                0d, 0d, 0d, 0d, 0d, 0d, 0d);
        }
        else
        {
            sourceForeground /= foregroundCount;
            sourceBackground /= backgroundCount;
            resultForeground /= foregroundCount;
            resultBackground /= backgroundCount;
            var scale = Math.Max(SpectralAmplitudeWriter.MinimumRobustScale,
                localScale / (foregroundCount + backgroundCount));
            var before = (sourceForeground - sourceBackground) / scale;
            var after = (resultForeground - resultBackground) / scale;
            visibility = new SpectralVisibilityDiagnostics(true, null, sourceForeground, sourceBackground,
                resultForeground, resultBackground, before, after, after - before);
        }

        var energyIncrease = write.SourceEnergy <= 0d
            ? write.ResultEnergy <= 0d ? 0d : double.PositiveInfinity
            : (write.ResultEnergy - write.SourceEnergy) / write.SourceEnergy;
        return new SpectralFrequencyDiagnostics(write.ChangedIndependentBins, write.ChangedTotalBins,
            write.ChangedTotalBins / (double)source.ValueCount, write.SourceEnergy, write.ResultEnergy,
            energyIncrease, write.MaximumPhaseDeviation, write.MaximumConjugateResidual, visibility);
    }

    public PixelImage CreateMappingPreview(
        SpectralPatternMapping mapping,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        var rgba = new byte[checked(mapping.SpectrumWidth * mapping.SpectrumHeight * 4)];
        for (var displayY = 0; displayY < mapping.SpectrumHeight; displayY++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var displayX = 0; displayX < mapping.SpectrumWidth; displayX++)
            {
                var point = FrequencyCoordinates.FromDisplay(displayX, displayY,
                    mapping.SpectrumWidth, mapping.SpectrumHeight);
                var protectedBin = point.Radius < SpectralPatternMapper.DcExclusionRadius ||
                                   Math.Abs(point.Kx) <= 1 || Math.Abs(point.Ky) <= 1 ||
                                   displayX <= 1 || displayY <= 1;
                var offset = ((displayY * mapping.SpectrumWidth) + displayX) * 4;
                rgba[offset] = protectedBin ? (byte)36 : (byte)6;
                rgba[offset + 1] = 6;
                rgba[offset + 2] = 6;
                rgba[offset + 3] = 255;
            }
        }

        for (var localY = 0; localY < mapping.Height; localY++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var localX = 0; localX < mapping.Width; localX++)
            {
                var weight = mapping[localX, localY];
                var level = (byte)Math.Clamp((int)Math.Round(weight * 255d), 0, 255);
                var displayX = mapping.Left + localX;
                var displayY = mapping.Top + localY;
                Set(displayX, displayY, 0, level, 0);
                var point = FrequencyCoordinates.FromDisplay(displayX, displayY,
                    mapping.SpectrumWidth, mapping.SpectrumHeight);
                var conjugate = FrequencyCoordinates.ConjugateIndex(point.InternalX, point.InternalY,
                    mapping.SpectrumWidth, mapping.SpectrumHeight);
                var mirror = FrequencyCoordinates.FromInternal(conjugate.X, conjugate.Y,
                    mapping.SpectrumWidth, mapping.SpectrumHeight);
                Set(mirror.DisplayX, mirror.DisplayY, 0, level, level);
            }
        }
        return new PixelImage(new ImageSize(mapping.SpectrumWidth, mapping.SpectrumHeight), rgba);

        void Set(int x, int y, byte red, byte green, byte blue)
        {
            var offset = ((y * mapping.SpectrumWidth) + x) * 4;
            rgba[offset] = red;
            rgba[offset + 1] = green;
            rgba[offset + 2] = blue;
            rgba[offset + 3] = 255;
        }
    }

    public PixelImage CreateSpectrumDifference(
        FrequencySpectrum source,
        Complex[] written,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(written);
        if (written.Length != source.ValueCount) throw new ArgumentException("频谱长度不一致。", nameof(written));
        var differences = new double[written.Length];
        double maximum = 0d;
        var original = source.Values.Span;
        for (var i = 0; i < written.Length; i++)
        {
            if ((i & 16383) == 0) cancellationToken.ThrowIfCancellationRequested();
            var difference = Math.Abs(LogPower(written[i]) - LogPower(original[i]));
            differences[i] = difference;
            maximum = Math.Max(maximum, difference);
        }
        var rgba = new byte[checked(written.Length * 4)];
        for (var displayY = 0; displayY < source.PaddedHeight; displayY++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var displayX = 0; displayX < source.PaddedWidth; displayX++)
            {
                var point = FrequencyCoordinates.FromDisplay(displayX, displayY,
                    source.PaddedWidth, source.PaddedHeight);
                var value = differences[(point.InternalY * source.PaddedWidth) + point.InternalX];
                var level = maximum <= 0d ? (byte)0 :
                    (byte)Math.Clamp((int)Math.Round(value / maximum * 255d), 0, 255);
                var offset = ((displayY * source.PaddedWidth) + displayX) * 4;
                rgba[offset] = level;
                rgba[offset + 1] = (byte)(level / 2);
                rgba[offset + 2] = 0;
                rgba[offset + 3] = 255;
            }
        }
        return new PixelImage(new ImageSize(source.PaddedWidth, source.PaddedHeight), rgba);
    }

    private static double LogPower(Complex value) =>
        Math.Log(1d + (value.Real * value.Real) + (value.Imaginary * value.Imaginary));
}
