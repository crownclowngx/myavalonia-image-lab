namespace ImageLabPlugin.Domain.Watermarking;

internal readonly record struct QimDecision(bool Bit, double Confidence);

/// <summary>使用量化索引奇偶性表达 bit，并提供读取置信度。</summary>
internal static class QimModulator
{
    public static double Embed(double coefficient, bool bit, double step)
    {
        ValidateStep(step);
        var nearest = (long)Math.Round(coefficient / step);
        if (((Math.Abs(nearest) & 1L) == 1L) != bit)
        {
            var lower = nearest - 1;
            var upper = nearest + 1;
            nearest = Math.Abs(coefficient - (lower * step)) <= Math.Abs(coefficient - (upper * step))
                ? lower
                : upper;
        }

        return nearest * step;
    }

    public static QimDecision Read(double coefficient, double step)
    {
        ValidateStep(step);
        var nearest = (long)Math.Round(coefficient / step);
        var error = Math.Abs(coefficient - (nearest * step));
        var confidence = Math.Clamp(1d - (error / (step / 2d)), 0d, 1d);
        return new QimDecision((Math.Abs(nearest) & 1L) == 1L, confidence);
    }

    private static void ValidateStep(double step)
    {
        if (!double.IsFinite(step) || step <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(step), step, "QIM 步长必须是有限正数。");
        }
    }
}
