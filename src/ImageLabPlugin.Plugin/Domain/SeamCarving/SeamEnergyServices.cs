using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Domain.SeamCarving;

/// <summary>把非预乘 RGBA8888 投影为固定白底上的 BT.601 全范围亮度。</summary>
/// <remarks>
/// 完全透明像素的 RGB 是不可见的“隐藏颜色”。若直接对原始 RGB 求梯度，路径会被用户看不见的信息驱动；
/// 因此这里先按 Alpha 合成白底，再计算亮度。该服务只负责颜色投影，不知道 Sobel 或蒙版偏置。
/// </remarks>
internal sealed class SeamLumaProjector
{
    public double[] Project(PixelImage image, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        var result = new double[checked((int)image.Size.PixelCount)];
        var rgba = image.Rgba.Span;
        for (var y = 0; y < image.Size.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = y * image.Size.Width;
            for (var x = 0; x < image.Size.Width; x++)
            {
                var offset = (row + x) * 4;
                var alpha = rgba[offset + 3] / 255d;
                var inverse = 1d - alpha;
                var red = (alpha * rgba[offset]) + (inverse * 255d);
                var green = (alpha * rgba[offset + 1]) + (inverse * 255d);
                var blue = (alpha * rgba[offset + 2]) + (inverse * 255d);
                result[row + x] = (0.299d * red) + (0.587d * green) + (0.114d * blue);
            }
        }
        return result;
    }
}

/// <summary>按冻结的 3×3 Sobel、clamp-to-edge 边界和 ±1000 区域偏置计算能量。</summary>
/// <remarks>
/// 归一化分母 4×255×sqrt(2) 是两个 Sobel 方向同时达到理论最大值时的模长。
/// 保护与优先删除只是很强的有限偏置，不是不可穿越的硬约束；目标过小时仍可能经过保护区。
/// </remarks>
internal sealed class SobelEnergyCalculator(SeamLumaProjector lumaProjector)
{
    internal const double ProtectBias = 1_000d;
    internal const double PreferRemovalBias = -1_000d;
    private static readonly double Normalization = 4d * 255d * Math.Sqrt(2d);

    public SeamEnergyMap Calculate(PixelImage image, SeamMask mask, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(mask);
        if (image.Size != mask.Size) throw new ArgumentException("图片与蒙版尺寸必须一致。", nameof(mask));
        var luma = lumaProjector.Project(image, cancellationToken);
        var count = checked((int)image.Size.PixelCount);
        var baseEnergy = new double[count];
        var effective = new double[count];
        var maskValues = mask.Values.Span;

        for (var y = 0; y < image.Size.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var top = Math.Max(0, y - 1);
            var bottom = Math.Min(image.Size.Height - 1, y + 1);
            for (var x = 0; x < image.Size.Width; x++)
            {
                var left = Math.Max(0, x - 1);
                var right = Math.Min(image.Size.Width - 1, x + 1);
                var topLeft = luma[(top * image.Size.Width) + left];
                var topCenter = luma[(top * image.Size.Width) + x];
                var topRight = luma[(top * image.Size.Width) + right];
                var middleLeft = luma[(y * image.Size.Width) + left];
                var middleRight = luma[(y * image.Size.Width) + right];
                var bottomLeft = luma[(bottom * image.Size.Width) + left];
                var bottomCenter = luma[(bottom * image.Size.Width) + x];
                var bottomRight = luma[(bottom * image.Size.Width) + right];

                // Gx 从左向右，Gy 从上向下；边界坐标钳制到最近像素，不补零也不镜像。
                var gx = (-topLeft + topRight) + (-2d * middleLeft + 2d * middleRight) +
                    (-bottomLeft + bottomRight);
                var gy = (-topLeft - 2d * topCenter - topRight) +
                    (bottomLeft + 2d * bottomCenter + bottomRight);
                var index = (y * image.Size.Width) + x;
                var energy = Math.Clamp(Math.Sqrt((gx * gx) + (gy * gy)) / Normalization, 0d, 1d);
                if (!double.IsFinite(energy)) throw new InvalidOperationException($"像素 ({x},{y}) 产生了非有限 Sobel 能量。");
                baseEnergy[index] = energy;
                effective[index] = maskValues[index] switch
                {
                    (byte)SeamMaskValue.Protect => energy + ProtectBias,
                    (byte)SeamMaskValue.PreferRemoval => energy + PreferRemovalBias,
                    _ => energy
                };
            }
        }

        return new SeamEnergyMap(image.Size, baseEnergy, effective, Summarize(baseEnergy));
    }

    private static SeamEnergySummary Summarize(double[] values)
    {
        var sorted = (double[])values.Clone();
        Array.Sort(sorted);
        var sum = 0d;
        long nonFinite = 0;
        foreach (var value in values)
        {
            if (double.IsFinite(value)) sum += value;
            else nonFinite++;
        }
        if (nonFinite != 0) throw new InvalidOperationException("能量摘要检测到非有限数，当前步骤已终止。");
        return new(sorted[0], sorted[^1], sum / values.Length,
            Percentile(sorted, 0.50d), Percentile(sorted, 0.95d), nonFinite);
    }

    private static double Percentile(double[] sorted, double percentile)
    {
        if (sorted.Length == 1) return sorted[0];
        var position = percentile * (sorted.Length - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper) return sorted[lower];
        return sorted[lower] + ((sorted[upper] - sorted[lower]) * (position - lower));
    }
}

/// <summary>把基础或有效能量投影为灰度 RGBA 预览；显示映射绝不回写算法能量。</summary>
internal sealed class SeamEnergyPreviewProjector
{
    public PixelImage Project(SeamEnergyMap map, bool effective, EnergyDisplayMode mode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        var source = effective ? map.EffectiveEnergy.Span : map.BaseEnergy.Span;
        var minimum = source[0];
        var maximum = source[0];
        foreach (var value in source) { minimum = Math.Min(minimum, value); maximum = Math.Max(maximum, value); }
        var range = maximum - minimum;
        var rgba = new byte[checked(source.Length * 4)];
        for (var y = 0; y < map.Size.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < map.Size.Width; x++)
            {
                var index = (y * map.Size.Width) + x;
                var normalized = range == 0d ? 0d : (source[index] - minimum) / range;
                if (mode == EnergyDisplayMode.Logarithmic) normalized = Math.Log(1d + (9d * normalized)) / Math.Log(10d);
                var value = (byte)Math.Clamp(Math.Round(normalized * 255d, MidpointRounding.ToEven), 0d, 255d);
                var offset = index * 4;
                rgba[offset] = rgba[offset + 1] = rgba[offset + 2] = value;
                rgba[offset + 3] = 255;
            }
        }
        return new PixelImage(map.Size, rgba);
    }
}
