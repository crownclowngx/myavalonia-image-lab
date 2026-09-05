using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Domain.Shared.Perturbations;

internal sealed class GaussianBlurOperator : SynchronousPerturbationOperator<GaussianBlurParameters>
{
    public override PerturbationKind Kind => PerturbationKind.GaussianBlur;
    protected override PixelImage Apply(PixelImage source, GaussianBlurParameters p, PerturbationExecutionContext trial, CancellationToken token) =>
        p.Sigma == 0m ? source.Clone() : GaussianBlur.Apply(source, (double)p.Sigma, token);
}

/// <summary>可分离高斯卷积。核半径为 ceil(3σ)，边界 clamp-to-edge，Alpha 原样保留。</summary>
internal static class GaussianBlur
{
    public static PixelImage Apply(PixelImage source, double sigma, CancellationToken token)
    {
        var radius = Math.Max(1, (int)Math.Ceiling(3d * sigma)); var kernel = new double[(radius * 2) + 1]; var sum = 0d;
        for (var i = -radius; i <= radius; i++) { var value = Math.Exp(-(i * i) / (2d * sigma * sigma)); kernel[i + radius] = value; sum += value; }
        for (var i = 0; i < kernel.Length; i++) kernel[i] /= sum;
        var sourceBytes = source.Rgba.Span; var width = source.Size.Width; var height = source.Size.Height;
        // 临时缓冲只保存 RGB double；垂直阶段立即量化输出，避免第二份完整 double 图。
        var horizontal = new double[checked(width * height * 3)];
        for (var y = 0; y < height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < width; x++)
                for (var c = 0; c < 3; c++)
                {
                    var value = 0d;
                    for (var k = -radius; k <= radius; k++) value += sourceBytes[PerturbationPixels.Offset(source.Size, Math.Clamp(x + k, 0, width - 1), y) + c] * kernel[k + radius];
                    horizontal[((y * width + x) * 3) + c] = value;
                }
        }
        var output = source.Clone(); var bytes = output.WritableRgba;
        for (var y = 0; y < height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < width; x++)
                for (var c = 0; c < 3; c++)
                {
                    var value = 0d;
                    for (var k = -radius; k <= radius; k++) value += horizontal[((Math.Clamp(y + k, 0, height - 1) * width + x) * 3) + c] * kernel[k + radius];
                    bytes[PerturbationPixels.Offset(source.Size, x, y) + c] = PerturbationPixels.ClampRound(value);
                }
        }
        return output;
    }
}

internal sealed class MedianBlurOperator : SynchronousPerturbationOperator<MedianBlurParameters>
{
    public override PerturbationKind Kind => PerturbationKind.MedianBlur;
    protected override PixelImage Apply(PixelImage source, MedianBlurParameters p, PerturbationExecutionContext trial, CancellationToken token)
    {
        var output = source.Clone(); var input = source.Rgba.Span; var bytes = output.WritableRgba; var radius = p.KernelSize / 2;
        Span<byte> samples = stackalloc byte[25];
        for (var y = 0; y < source.Size.Height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < source.Size.Width; x++)
                for (var c = 0; c < 3; c++)
                {
                    var count = 0;
                    for (var ky = -radius; ky <= radius; ky++)
                        for (var kx = -radius; kx <= radius; kx++)
                            samples[count++] = input[PerturbationPixels.Offset(source.Size, Math.Clamp(x + kx, 0, source.Size.Width - 1), Math.Clamp(y + ky, 0, source.Size.Height - 1)) + c];
                    samples[..count].Sort(); bytes[PerturbationPixels.Offset(source.Size, x, y) + c] = samples[count / 2];
                }
        }
        return output;
    }
}

internal sealed class UnsharpMaskOperator : SynchronousPerturbationOperator<UnsharpMaskParameters>
{
    public override PerturbationKind Kind => PerturbationKind.UnsharpMask;
    protected override PixelImage Apply(PixelImage source, UnsharpMaskParameters p, PerturbationExecutionContext trial, CancellationToken token)
    {
        if (p.Amount == 0m) return source.Clone();
        var blurred = GaussianBlur.Apply(source, 1d, token); var output = source.Clone(); var original = source.Rgba.Span; var blur = blurred.Rgba.Span; var bytes = output.WritableRgba; var amount = (double)p.Amount;
        for (var y = 0; y < source.Size.Height; y++)
        {
            token.ThrowIfCancellationRequested();
            var end = (y + 1) * source.Size.Width * 4;
            for (var o = y * source.Size.Width * 4; o < end; o += 4)
                for (var c = 0; c < 3; c++) bytes[o + c] = PerturbationPixels.ClampRound(original[o + c] + (amount * (original[o + c] - blur[o + c])));
        }
        return output;
    }
}
