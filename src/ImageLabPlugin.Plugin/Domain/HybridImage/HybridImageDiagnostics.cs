using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Domain.HybridImage;

internal sealed record HybridAlignmentDiagnostics(
    double Scale,
    double RotationDegrees,
    double TranslateX,
    double TranslateY,
    HybridResidualStatus ResidualStatus,
    double RmsResidualPixels,
    double MaximumResidualPixels,
    double CoverageRatio,
    string Explanation);

/// <summary>只计算对齐和重影的可解释事实，不尝试给不同主体判定“配准正确”。</summary>
internal sealed class HybridImageDiagnostics
{
    public HybridAlignmentDiagnostics Describe(HybridAlignmentSolution solution, double coverageRatio) =>
        new(solution.Transform.Scale, solution.Transform.RotationDegrees, solution.Transform.TranslateX,
            solution.Transform.TranslateY, solution.ResidualStatus, solution.RmsResidualPixels,
            solution.MaximumResidualPixels, coverageRatio,
            solution.ResidualStatus == HybridResidualStatus.NotIndependentlyValidated
                ? "两点足以精确拟合，但没有冗余点独立验证残差。"
                : "残差只描述控制点重投影；内容本就不同的边缘不要求重合。");

    /// <summary>以 A Sobel 边缘为红、B Sobel 边缘为青；共同强边缘趋近白色。</summary>
    public PixelImage CreateRedCyanEdgeOverlay(HybridLumaPlane a, HybridLumaPlane b,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (a.Size != b.Size) throw new ArgumentException("重影叠加要求相同尺寸。", nameof(b));
        var edgeA = Sobel(a, cancellationToken);
        var edgeB = Sobel(b, cancellationToken);
        var rgba = new byte[checked((int)(a.Size.PixelCount * 4))];
        for (var i = 0; i < edgeA.Length; i++)
        {
            var red = ToByte(edgeA[i]);
            var cyan = ToByte(edgeB[i]);
            var offset = i * 4;
            rgba[offset] = red;
            rgba[offset + 1] = cyan;
            rgba[offset + 2] = cyan;
            rgba[offset + 3] = 255;
        }
        return new PixelImage(a.Size, rgba);
    }

    /// <summary>把有符号高频以 0.5 中性灰显示；该偏移只用于观察，绝不返回数值管线。</summary>
    public PixelImage CreateSignedComponentPreview(HybridLumaPlane component, double displayGain = 2d,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(component);
        if (!double.IsFinite(displayGain) || displayGain <= 0d)
            throw new ArgumentOutOfRangeException(nameof(displayGain));
        var rgba = new byte[checked((int)(component.Size.PixelCount * 4))];
        for (var i = 0; i < component.Values.Length; i++)
        {
            if ((i & 16383) == 0) cancellationToken.ThrowIfCancellationRequested();
            var level = ToByte(Math.Clamp(0.5d + (component.Values.Span[i] * displayGain), 0d, 1d));
            var offset = i * 4;
            rgba[offset] = rgba[offset + 1] = rgba[offset + 2] = level;
            rgba[offset + 3] = 255;
        }
        return new PixelImage(component.Size, rgba);
    }

    private static double[] Sobel(HybridLumaPlane plane, CancellationToken cancellationToken)
    {
        var output = new double[checked((int)plane.Size.PixelCount)];
        for (var y = 0; y < plane.Size.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < plane.Size.Width; x++)
            {
                double gx = 0d, gy = 0d;
                for (var ky = -1; ky <= 1; ky++)
                    for (var kx = -1; kx <= 1; kx++)
                    {
                        var sample = plane[GaussianPlaneFilter.Reflect101(x + kx, plane.Size.Width),
                            GaussianPlaneFilter.Reflect101(y + ky, plane.Size.Height)];
                        gx += sample * (kx switch { -1 => -1d * (ky == 0 ? 2d : 1d), 1 => ky == 0 ? 2d : 1d, _ => 0d });
                        gy += sample * (ky switch { -1 => -1d * (kx == 0 ? 2d : 1d), 1 => kx == 0 ? 2d : 1d, _ => 0d });
                    }
                output[(y * plane.Size.Width) + x] = Math.Min(1d, Math.Sqrt((gx * gx) + (gy * gy)) / 4d);
            }
        }
        return output;
    }

    private static byte ToByte(double value) => (byte)Math.Clamp((int)Math.Round(value * 255d,
        MidpointRounding.ToEven), 0, 255);
}

/// <summary>把 RGBA 在白色背景合成后转换为 [0,1] 确定性亮度。</summary>
internal sealed class HybridLumaProjector
{
    public HybridLumaPlane Project(PixelImage source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var values = new double[checked((int)source.Size.PixelCount)];
        for (var y = 0; y < source.Size.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < source.Size.Width; x++)
            {
                var (red, green, blue, alphaByte) = source.GetPixel(x, y);
                var alpha = alphaByte / 255d;
                var compositeR = (red * alpha) + (255d * (1d - alpha));
                var compositeG = (green * alpha) + (255d * (1d - alpha));
                var compositeB = (blue * alpha) + (255d * (1d - alpha));
                values[(y * source.Size.Width) + x] =
                    ((0.299d * compositeR) + (0.587d * compositeG) + (0.114d * compositeB)) / 255d;
            }
        }
        return new HybridLumaPlane(source.Size, values);
    }
}
