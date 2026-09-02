using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Domain.Shared.Perturbations;

internal sealed class DeterministicPixelOperator : SynchronousPerturbationOperator<DeterministicPixelParameters>
{
    public override PerturbationKind Kind => PerturbationKind.DeterministicPixel;
    protected override PixelImage Apply(PixelImage source, DeterministicPixelParameters p, PerturbationExecutionContext trial, CancellationToken token)
    {
        var output = source.Clone(); if (p.Amplitude == 0) return output;
        var bytes = output.WritableRgba;
        for (var y = 0; y < source.Size.Height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < source.Size.Width; x++)
            {
                var offset = PerturbationPixels.Offset(source.Size, x, y);
                for (var channel = 0; channel < 3; channel++)
                {
                    var sign = ((x + y + channel) & 1) == 0 ? 1 : -1;
                    bytes[offset + channel] = PerturbationPixels.ClampRound(bytes[offset + channel] + (sign * p.Amplitude));
                }
            }
        }
        return output;
    }
}

internal sealed class GaussianNoiseOperator : SynchronousPerturbationOperator<GaussianNoiseParameters>
{
    public override PerturbationKind Kind => PerturbationKind.GaussianNoise;
    /// <summary>Box–Muller 公式 z=sqrt(-2 ln u1) cos(2πu2)，一次只保留一个备用样本，不分配全图噪声场。</summary>
    protected override PixelImage Apply(PixelImage source, GaussianNoiseParameters p, PerturbationExecutionContext trial, CancellationToken token)
    {
        var output = source.Clone(); if (p.Sigma == 0m) return output;
        var bytes = output.WritableRgba; var random = trial.CreateRandom(); var sigma = (double)p.Sigma;
        var hasSpare = false; var spare = 0d;
        for (var y = 0; y < source.Size.Height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < source.Size.Width; x++)
            {
                var offset = PerturbationPixels.Offset(source.Size, x, y);
                for (var channel = 0; channel < 3; channel++)
                {
                    double normal;
                    if (hasSpare) { normal = spare; hasSpare = false; }
                    else
                    {
                        var u1 = Math.Max(double.Epsilon, random.NextDouble()); var u2 = random.NextDouble();
                        var radius = Math.Sqrt(-2d * Math.Log(u1));
                        normal = radius * Math.Cos(2d * Math.PI * u2); spare = radius * Math.Sin(2d * Math.PI * u2); hasSpare = true;
                    }
                    bytes[offset + channel] = PerturbationPixels.ClampRound(bytes[offset + channel] + (normal * sigma));
                }
            }
        }
        return output;
    }
}

internal sealed class SaltPepperNoiseOperator : SynchronousPerturbationOperator<SaltPepperParameters>
{
    public override PerturbationKind Kind => PerturbationKind.SaltPepperNoise;
    protected override PixelImage Apply(PixelImage source, SaltPepperParameters p, PerturbationExecutionContext trial, CancellationToken token)
    {
        var output = source.Clone(); if (p.Ratio == 0m) return output;
        var random = trial.CreateRandom(); var target = (long)Math.Round(source.Size.PixelCount * (double)p.Ratio, MidpointRounding.AwayFromZero);
        var remainingToSelect = target; var remainingPixels = source.Size.PixelCount; var bytes = output.WritableRgba;
        // 顺序无放回抽样以 O(1) 辅助内存选择精确数量，避免高比例时构建全图索引或 HashSet。
        for (var index = 0L; index < source.Size.PixelCount && remainingToSelect > 0; index++, remainingPixels--)
        {
            if ((index % source.Size.Width) == 0) token.ThrowIfCancellationRequested();
            if (random.NextDouble() * remainingPixels >= remainingToSelect) continue;
            remainingToSelect--;
            var value = random.NextUInt64() % 2 == 0 ? (byte)0 : (byte)255;
            var offset = checked((int)(index * 4)); bytes[offset] = bytes[offset + 1] = bytes[offset + 2] = value;
        }
        return output;
    }
}

internal sealed class BrightnessOperator : SynchronousPerturbationOperator<BrightnessParameters>
{
    public override PerturbationKind Kind => PerturbationKind.Brightness;
    protected override PixelImage Apply(PixelImage source, BrightnessParameters p, PerturbationExecutionContext trial, CancellationToken token) =>
        Transform(source, token, value => value + p.Offset);
    internal static PixelImage Transform(PixelImage source, CancellationToken token, Func<byte, double> transform)
    {
        var output = source.Clone(); var bytes = output.WritableRgba;
        for (var y = 0; y < source.Size.Height; y++)
        {
            token.ThrowIfCancellationRequested();
            var end = (y + 1) * source.Size.Width * 4;
            for (var offset = y * source.Size.Width * 4; offset < end; offset += 4)
                for (var channel = 0; channel < 3; channel++) bytes[offset + channel] = PerturbationPixels.ClampRound(transform(bytes[offset + channel]));
        }
        return output;
    }
}

internal sealed class ContrastOperator : SynchronousPerturbationOperator<ContrastParameters>
{
    public override PerturbationKind Kind => PerturbationKind.Contrast;
    protected override PixelImage Apply(PixelImage source, ContrastParameters p, PerturbationExecutionContext trial, CancellationToken token) =>
        BrightnessOperator.Transform(source, token, value => 127.5d + ((value - 127.5d) * (double)p.Factor));
}

internal sealed class GammaOperator : SynchronousPerturbationOperator<GammaParameters>
{
    public override PerturbationKind Kind => PerturbationKind.Gamma;
    /// <summary>冻结公式 output=255×(input/255)^(1/gamma)，而不是使用含义相反的 gamma 指数。</summary>
    protected override PixelImage Apply(PixelImage source, GammaParameters p, PerturbationExecutionContext trial, CancellationToken token) =>
        BrightnessOperator.Transform(source, token, value => 255d * Math.Pow(value / 255d, 1d / (double)p.Gamma));
}

internal sealed class SaturationOperator : SynchronousPerturbationOperator<SaturationParameters>
{
    public override PerturbationKind Kind => PerturbationKind.Saturation;
    protected override PixelImage Apply(PixelImage source, SaturationParameters p, PerturbationExecutionContext trial, CancellationToken token)
    {
        var output = source.Clone(); if (p.Factor == 1m) return output;
        var bytes = output.WritableRgba; var factor = (double)p.Factor;
        for (var y = 0; y < source.Size.Height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < source.Size.Width; x++)
            {
                var o = PerturbationPixels.Offset(source.Size, x, y);
                var luma = (0.2126d * bytes[o]) + (0.7152d * bytes[o + 1]) + (0.0722d * bytes[o + 2]);
                for (var c = 0; c < 3; c++) bytes[o + c] = PerturbationPixels.ClampRound(luma + ((bytes[o + c] - luma) * factor));
            }
        }
        return output;
    }
}

internal sealed class ColorBiasOperator : SynchronousPerturbationOperator<ColorBiasParameters>
{
    public override PerturbationKind Kind => PerturbationKind.ColorBias;
    protected override PixelImage Apply(PixelImage source, ColorBiasParameters p, PerturbationExecutionContext trial, CancellationToken token)
    {
        var output = source.Clone(); var bytes = output.WritableRgba;
        for (var y = 0; y < source.Size.Height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < source.Size.Width; x++)
            {
                var o = PerturbationPixels.Offset(source.Size, x, y);
                bytes[o] = PerturbationPixels.ClampRound(bytes[o] + p.Red); bytes[o + 1] = PerturbationPixels.ClampRound(bytes[o + 1] + p.Green); bytes[o + 2] = PerturbationPixels.ClampRound(bytes[o + 2] + p.Blue);
            }
        }
        return output;
    }
}
