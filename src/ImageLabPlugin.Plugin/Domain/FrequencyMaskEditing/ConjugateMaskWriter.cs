using ImageLabPlugin.Domain.Shared.Spectral;

namespace ImageLabPlugin.Domain.FrequencyMaskEditing;

/// <summary>把一次混合原子写入自然索引频点及其共轭点。</summary>
internal sealed class ConjugateMaskWriter
{
    public void Mix(double[] gains, int width, int height, int internalX, int internalY,
        double targetGain, double opacity, FrequencyBandLock? bandLock = null)
    {
        ArgumentNullException.ThrowIfNull(gains);
        if (gains.Length != checked(width * height)) throw new ArgumentException("遮罩长度与尺寸不一致。", nameof(gains));
        if (!double.IsFinite(targetGain) || targetGain is < 0d or > 1d ||
            !double.IsFinite(opacity) || opacity is <= 0d or > 1d)
            throw new ArgumentOutOfRangeException(nameof(targetGain));

        var point = FrequencyCoordinates.FromInternal(internalX, internalY, width, height);
        var conjugate = FrequencyCoordinates.ConjugateIndex(internalX, internalY, width, height);
        var pairedPoint = FrequencyCoordinates.FromInternal(conjugate.X, conjugate.Y, width, height);
        if (bandLock is { } band && (!band.Contains(point.Radius) || !band.Contains(pairedPoint.Radius))) return;

        var index = (internalY * width) + internalX;
        var pairedIndex = (conjugate.Y * width) + conjugate.X;
        var mixed = Math.Clamp(gains[index] + (opacity * (targetGain - gains[index])), 0d, 1d);
        if (!double.IsFinite(mixed)) throw new InvalidDataException("遮罩混合产生了非有限值。");
        gains[index] = mixed;
        // DC 与 Nyquist 组合点可能与自身共轭。此时只应用一次 opacity，否则结果会依赖实现细节。
        if (pairedIndex != index) gains[pairedIndex] = mixed;
    }
}
