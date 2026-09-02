using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Domain.Shared.Spectral;

/// <summary>把只读频谱乘实数增益并投影处理后的精确频谱预览。</summary>
/// <remarks>
/// 该服务只做“频谱乘增益”这一件事，不执行 IFFT、通道回写或损失诊断。预览来自原复频谱 <c>F</c> 与遮罩 <c>H</c>
/// 的精确乘积，而不是对量化 PNG 再做一次 FFT；因此 Frequency Filter 与遮罩编辑器仍可继续复用原有重建核心。
/// </remarks>
internal sealed class FrequencyGainSpectrumProjector(SpectrumProjector projector)
{
    public (FrequencySpectrum Spectrum, PixelImage Preview) Project(FrequencySpectrum source,
        FrequencyGainMask mask, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(mask);
        if (source.PaddedWidth != mask.Width || source.PaddedHeight != mask.Height)
            throw new ArgumentException("频谱与增益遮罩尺寸不一致。", nameof(mask));
        var values = source.CreateWorkingCopy();
        var gains = mask.GainSpan;
        for (var i = 0; i < values.Length; i++)
        {
            if ((i & 16383) == 0) cancellationToken.ThrowIfCancellationRequested();
            values[i] *= gains[i];
        }
        var filtered = new FrequencySpectrum(source.SourceSize, source.PaddedWidth, source.PaddedHeight, values);
        return (filtered, projector.CreateMagnitude(filtered, SpectrumMagnitudeMode.Logarithmic, cancellationToken));
    }
}
