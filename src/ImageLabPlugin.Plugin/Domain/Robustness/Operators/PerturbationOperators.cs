using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Domain.Robustness.Operators;

/// <summary>有序扰动链的朴素 Strategy 边界；实现不得修改输入图片。</summary>
internal interface IImagePerturbationOperator
{
    PerturbationKind Kind { get; }
    ValueTask<PixelImage> ApplyAsync(PixelImage source, PerturbationParameters parameters, DeterministicTrialContext trial, CancellationToken cancellationToken);
}

internal abstract class SynchronousPerturbationOperator<TParameters> : IImagePerturbationOperator
    where TParameters : PerturbationParameters
{
    public abstract PerturbationKind Kind { get; }

    public ValueTask<PixelImage> ApplyAsync(PixelImage source, PerturbationParameters parameters, DeterministicTrialContext trial, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (parameters is not TParameters typed) throw new ArgumentException($"{Kind.ToStableId()} 收到错误参数类型。", nameof(parameters));
        PerturbationParameterEditor.Validate(new PerturbationStep(trial.StepId, Kind, true, typed));
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Apply(source, typed, trial, cancellationToken));
    }

    protected abstract PixelImage Apply(PixelImage source, TParameters parameters, DeterministicTrialContext trial, CancellationToken cancellationToken);
}

internal static class PerturbationPixels
{
    public static byte ClampRound(double value) => (byte)Math.Clamp((int)Math.Round(value, MidpointRounding.AwayFromZero), 0, 255);
    public static int Offset(ImageSize size, int x, int y) => checked(((y * size.Width) + x) * 4);
    public static void Write(Span<byte> target, int offset, RgbaColor color)
    {
        target[offset] = color.R; target[offset + 1] = color.G; target[offset + 2] = color.B; target[offset + 3] = color.A;
    }

    /// <summary>未预乘 RGBA 的双线性采样。越界点由调用方先处理，四个通道采用统一的四舍五入规则。</summary>
    public static void Bilinear(ReadOnlySpan<byte> source, ImageSize size, double x, double y, Span<byte> destination, int destinationOffset)
    {
        var x0 = Math.Clamp((int)Math.Floor(x), 0, size.Width - 1);
        var y0 = Math.Clamp((int)Math.Floor(y), 0, size.Height - 1);
        var x1 = Math.Min(x0 + 1, size.Width - 1);
        var y1 = Math.Min(y0 + 1, size.Height - 1);
        var fx = Math.Clamp(x - x0, 0d, 1d); var fy = Math.Clamp(y - y0, 0d, 1d);
        var o00 = Offset(size, x0, y0); var o10 = Offset(size, x1, y0); var o01 = Offset(size, x0, y1); var o11 = Offset(size, x1, y1);
        for (var channel = 0; channel < 4; channel++)
        {
            var top = source[o00 + channel] + ((source[o10 + channel] - source[o00 + channel]) * fx);
            var bottom = source[o01 + channel] + ((source[o11 + channel] - source[o01 + channel]) * fx);
            destination[destinationOffset + channel] = ClampRound(top + ((bottom - top) * fy));
        }
    }
}
