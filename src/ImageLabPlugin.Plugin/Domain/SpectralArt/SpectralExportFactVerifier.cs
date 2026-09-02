using System.Numerics;
using ImageLabPlugin.Domain.Shared.Spectral;
using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Domain.SpectralArt;

internal sealed record SpectralExportFacts(int PaddedWidth, int PaddedHeight,
    string MappingFingerprint, double MaximumNormalizedConjugateResidual);

/// <summary>对 PNG 内存回读图重新建立 Y 频谱并验证导出所依赖的频域事实。</summary>
/// <remarks>
/// 该检查只在导出阶段短暂创建一份回读频谱，不与 Render 工作副本同时存在。映射指纹证明同一 Pattern、Region、
/// Fit 与补零尺寸仍成立；实值图片的 FFT 必须保持归一化共轭残差不超过 1E-10。使用归一化残差是为了避免大图
/// DC 量级把机器舍入误差误判为业务失败，同时任何非有限系数都会直接拒绝发布。
/// </remarks>
internal sealed class SpectralExportFactVerifier(
    ImageChannelConverter channelConverter,
    FrequencySpectrumBuilder spectrumBuilder,
    SpectralPatternMapper mapper)
{
    private const double MaximumNormalizedConjugateResidual = 1e-10d;

    public SpectralExportFacts Verify(PixelImage decoded,
        SpectralPattern pattern, SpectralArtRegion region, SpectralPatternFitMode fitMode,
        string expectedMappingFingerprint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(decoded); ArgumentNullException.ThrowIfNull(pattern);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedMappingFingerprint);
        var luma = channelConverter.Extract(decoded, ImageChannel.Luma, cancellationToken);
        var spectrum = spectrumBuilder.Build(luma, cancellationToken);
        var mapping = mapper.Map(pattern, region, fitMode, spectrum.PaddedWidth, spectrum.PaddedHeight,
            cancellationToken);
        if (!StringComparer.Ordinal.Equals(mapping.Fingerprint, expectedMappingFingerprint))
            throw new InvalidDataException("PNG 回读后的频谱尺寸或 Pattern 映射事实与渲染结果不一致。");

        var values = spectrum.Values.Span;
        double maximum = 0d;
        for (var y = 0; y < spectrum.PaddedHeight; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < spectrum.PaddedWidth; x++)
            {
                var index = (y * spectrum.PaddedWidth) + x;
                var value = values[index];
                if (!double.IsFinite(value.Real) || !double.IsFinite(value.Imaginary))
                    throw new InvalidDataException("PNG 回读频谱包含非有限系数。");
                var pair = FrequencyCoordinates.ConjugateIndex(x, y,
                    spectrum.PaddedWidth, spectrum.PaddedHeight);
                var mirror = values[(pair.Y * spectrum.PaddedWidth) + pair.X];
                var denominator = Math.Max(1d, Math.Max(value.Magnitude, mirror.Magnitude));
                maximum = Math.Max(maximum, Complex.Abs(mirror - Complex.Conjugate(value)) / denominator);
            }
        }
        if (maximum > MaximumNormalizedConjugateResidual)
            throw new InvalidDataException($"PNG 回读频谱共轭残差 {maximum:E3} 超出门禁。");
        return new SpectralExportFacts(spectrum.PaddedWidth, spectrum.PaddedHeight,
            mapping.Fingerprint, maximum);
    }
}
