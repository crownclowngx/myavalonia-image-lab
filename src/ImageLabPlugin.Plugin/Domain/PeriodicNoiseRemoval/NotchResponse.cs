namespace ImageLabPlugin.Domain.PeriodicNoiseRemoval;

/// <summary>计算一个频点到单个中心的振幅增益。</summary>
/// <remarks>
/// <c>Strength</c> 是复频谱振幅的衰减量而不是功率 dB。Butterworth 与 Gaussian 在 <c>d=r</c> 时均达到
/// 中心衰减的一半；完整 switch 明确覆盖 V1 三种固定过渡，不为没有独立生命周期的公式建立 Strategy 层次。
/// </remarks>
internal sealed class NotchResponse
{
    public double Gain(double distance, PeriodicNotchTransition transition, double radius, double strength, int order)
    {
        if (!double.IsFinite(distance) || distance < 0d) throw new ArgumentOutOfRangeException(nameof(distance));
        if (!double.IsFinite(radius) || radius <= 0d || radius > 0.25d) throw new ArgumentOutOfRangeException(nameof(radius));
        if (!double.IsFinite(strength) || strength is < 0d or > 1d) throw new ArgumentOutOfRangeException(nameof(strength));
        if (!Enum.IsDefined(transition)) throw new ArgumentOutOfRangeException(nameof(transition));
        if (transition == PeriodicNotchTransition.Butterworth && order is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(order));
        if (strength == 0d) return 1d;

        var ratio = distance / radius;
        var attenuation = transition switch
        {
            PeriodicNotchTransition.Ideal => distance <= radius ? strength : 0d,
            PeriodicNotchTransition.Butterworth => ButterworthAttenuation(ratio, strength, order),
            PeriodicNotchTransition.Gaussian => strength * Math.Exp(-Math.Log(2d) * ratio * ratio),
            _ => throw new ArgumentOutOfRangeException(nameof(transition))
        };
        return Math.Clamp(double.IsFinite(attenuation) ? 1d - attenuation : 1d, 0d, 1d);
    }

    private static double ButterworthAttenuation(double ratio, double strength, int order)
    {
        if (ratio == 0d) return strength;
        var powered = Math.Pow(ratio, 2d * order);
        return double.IsPositiveInfinity(powered) ? 0d : strength / (1d + powered);
    }
}
