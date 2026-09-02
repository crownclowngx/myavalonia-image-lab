using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Domain.SeamCarving;

/// <summary>普通规则网格缩放的窄 Strategy 契约；仅此处存在两个真实可替换实现。</summary>
internal interface IReferenceImageResampler
{
    ReferenceResizeAlgorithm Algorithm { get; }
    string StableId { get; }
    PixelImage Resize(PixelImage source, ImageSize target, CancellationToken cancellationToken = default);
}

/// <summary>使用像素中心逆向映射与 clamp 边界的双线性参考缩放。</summary>
internal sealed class BilinearReferenceResampler : IReferenceImageResampler
{
    public ReferenceResizeAlgorithm Algorithm => ReferenceResizeAlgorithm.Bilinear;
    public string StableId => "bilinear-premultiplied-v1";

    public PixelImage Resize(PixelImage source, ImageSize target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Size == target) return source.Clone();
        var output = new byte[checked((int)target.PixelCount * 4)];
        Span<double> sample = stackalloc double[4];
        for (var y = 0; y < target.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceY = ((y + 0.5d) * source.Size.Height / target.Height) - 0.5d;
            var y0 = Math.Clamp((int)Math.Floor(sourceY), 0, source.Size.Height - 1);
            var y1 = Math.Clamp(y0 + 1, 0, source.Size.Height - 1);
            var amountY = Math.Clamp(sourceY - Math.Floor(sourceY), 0d, 1d);
            for (var x = 0; x < target.Width; x++)
            {
                sample.Clear();
                var sourceX = ((x + 0.5d) * source.Size.Width / target.Width) - 0.5d;
                var x0 = Math.Clamp((int)Math.Floor(sourceX), 0, source.Size.Width - 1);
                var x1 = Math.Clamp(x0 + 1, 0, source.Size.Width - 1);
                var amountX = Math.Clamp(sourceX - Math.Floor(sourceX), 0d, 1d);
                AddPixel(source, x0, y0, (1d - amountX) * (1d - amountY), sample);
                AddPixel(source, x1, y0, amountX * (1d - amountY), sample);
                AddPixel(source, x0, y1, (1d - amountX) * amountY, sample);
                AddPixel(source, x1, y1, amountX * amountY, sample);
                WriteSample(output, (y * target.Width) + x, sample);
            }
        }
        return new PixelImage(target, output);
    }

    internal static void AddPixel(PixelImage source, int x, int y, double weight, Span<double> sample)
    {
        var pixel = source.GetPixel(x, y);
        var alpha = pixel.A / 255d;
        sample[0] += pixel.R / 255d * alpha * weight;
        sample[1] += pixel.G / 255d * alpha * weight;
        sample[2] += pixel.B / 255d * alpha * weight;
        sample[3] += alpha * weight;
    }

    internal static void WriteSample(Span<byte> output, int pixelIndex, ReadOnlySpan<double> sample)
    {
        var offset = pixelIndex * 4;
        var alpha = Math.Clamp(sample[3], 0d, 1d);
        if (alpha == 0d)
        {
            output.Slice(offset, 4).Clear();
            return;
        }
        output[offset] = SeamInserter.ToByte(Math.Clamp(sample[0], 0d, alpha) / alpha * 255d);
        output[offset + 1] = SeamInserter.ToByte(Math.Clamp(sample[1], 0d, alpha) / alpha * 255d);
        output[offset + 2] = SeamInserter.ToByte(Math.Clamp(sample[2], 0d, alpha) / alpha * 255d);
        output[offset + 3] = SeamInserter.ToByte(alpha * 255d);
    }
}

/// <summary>使用 Catmull–Rom 核（a=-0.5）的 4×4 双三次参考缩放。</summary>
/// <remarks>
/// 负核权重会过冲，因此累计 Alpha 钳制到 [0,1]，预乘颜色逐通道钳制到 [0,Alpha] 后再反预乘。
/// 该限制既保持常量图，也避免产生 RGB 大于 Alpha 的非法预乘中间值。
/// </remarks>
internal sealed class BicubicReferenceResampler : IReferenceImageResampler
{
    public ReferenceResizeAlgorithm Algorithm => ReferenceResizeAlgorithm.BicubicCatmullRom;
    public string StableId => "bicubic-catmull-rom-premultiplied-v1";

    public PixelImage Resize(PixelImage source, ImageSize target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Size == target) return source.Clone();
        var output = new byte[checked((int)target.PixelCount * 4)];
        Span<double> sample = stackalloc double[4];
        for (var y = 0; y < target.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceY = ((y + 0.5d) * source.Size.Height / target.Height) - 0.5d;
            var baseY = (int)Math.Floor(sourceY);
            for (var x = 0; x < target.Width; x++)
            {
                sample.Clear();
                var sourceX = ((x + 0.5d) * source.Size.Width / target.Width) - 0.5d;
                var baseX = (int)Math.Floor(sourceX);
                for (var offsetY = -1; offsetY <= 2; offsetY++)
                {
                    var weightY = Kernel(sourceY - (baseY + offsetY));
                    var sampleY = Math.Clamp(baseY + offsetY, 0, source.Size.Height - 1);
                    for (var offsetX = -1; offsetX <= 2; offsetX++)
                    {
                        var weightX = Kernel(sourceX - (baseX + offsetX));
                        var sampleX = Math.Clamp(baseX + offsetX, 0, source.Size.Width - 1);
                        BilinearReferenceResampler.AddPixel(source, sampleX, sampleY, weightX * weightY, sample);
                    }
                }
                BilinearReferenceResampler.WriteSample(output, (y * target.Width) + x, sample);
            }
        }
        return new PixelImage(target, output);
    }

    internal static double Kernel(double distance)
    {
        var absolute = Math.Abs(distance);
        if (absolute <= 1d) return (1.5d * absolute * absolute * absolute) -
            (2.5d * absolute * absolute) + 1d;
        if (absolute < 2d) return (-0.5d * absolute * absolute * absolute) +
            (2.5d * absolute * absolute) - (4d * absolute) + 2d;
        return 0d;
    }
}
