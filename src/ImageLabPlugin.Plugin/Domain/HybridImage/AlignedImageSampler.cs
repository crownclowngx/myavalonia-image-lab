using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Domain.HybridImage;

internal sealed record HybridWarpResult(HybridLumaPlane AlignedB, ReadOnlyMemory<bool> ValidMask)
{
    public bool IsValid(int x, int y) => ValidMask.Span[(y * AlignedB.Size.Width) + x];
}

/// <summary>把 B 的亮度平面逆向采样到 A 的像素中心栅格。</summary>
/// <remarks>
/// 对每个 A 像素中心先应用一次解析逆变换，再减 0.5 转为 B 的数组索引坐标。只有双线性所需的
/// 四个邻点都存在时才标记有效；越界不 Clamp，从而不会把边缘像素伪造成真实重叠内容。
/// </remarks>
internal sealed class AlignedImageSampler
{
    public HybridWarpResult Warp(
        HybridLumaPlane sourceB,
        ImageSize targetSizeA,
        HybridSimilarityTransform transformBToA,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceB);
        var values = new double[checked((int)targetSizeA.PixelCount)];
        var valid = new bool[values.Length];
        var source = sourceB.Values.Span;
        for (var y = 0; y < targetSizeA.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < targetSizeA.Width; x++)
            {
                var bCenter = transformBToA.MapAToB(new HybridPoint(x + 0.5d, y + 0.5d));
                var bx = bCenter.X - 0.5d;
                var by = bCenter.Y - 0.5d;
                var left = (int)Math.Floor(bx);
                var top = (int)Math.Floor(by);
                var index = (y * targetSizeA.Width) + x;
                if (left < 0 || top < 0 || left + 1 >= sourceB.Size.Width || top + 1 >= sourceB.Size.Height)
                    continue;

                var dx = bx - left;
                var dy = by - top;
                var row0 = top * sourceB.Size.Width;
                var row1 = (top + 1) * sourceB.Size.Width;
                // 累加顺序固定为左上、右上、左下、右下，确保不同调用路径得到相同 double 舍入。
                var value = source[row0 + left] * ((1d - dx) * (1d - dy));
                value += source[row0 + left + 1] * (dx * (1d - dy));
                value += source[row1 + left] * ((1d - dx) * dy);
                value += source[row1 + left + 1] * (dx * dy);
                values[index] = value;
                valid[index] = true;
            }
        }
        return new HybridWarpResult(new HybridLumaPlane(targetSizeA, values), valid);
    }
}

/// <summary>从有效掩码寻找确定性的最大轴对齐矩形，并校验用户裁切。</summary>
/// <remarks>
/// 每行把连续有效高度视为直方图，以单调栈枚举 O(width×height) 候选。面积相同依次选择更靠上、
/// 更靠左、更矮、更窄，避免运行时或遍历细节改变默认配方。
/// </remarks>
internal sealed class HybridCropValidator
{
    public HybridCropRectangle FindMaximumValidRectangle(
        ImageSize size,
        ReadOnlySpan<bool> validMask,
        CancellationToken cancellationToken = default)
    {
        if (validMask.Length != size.PixelCount) throw new ArgumentException("有效掩码长度与尺寸不一致。", nameof(validMask));
        var heights = new int[size.Width];
        HybridCropRectangle? best = null;
        for (var y = 0; y < size.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < size.Width; x++)
                heights[x] = validMask[(y * size.Width) + x] ? checked(heights[x] + 1) : 0;
            var stack = new Stack<int>();
            for (var x = 0; x <= size.Width; x++)
            {
                var height = x == size.Width ? 0 : heights[x];
                while (stack.Count > 0 && heights[stack.Peek()] >= height)
                {
                    var bar = stack.Pop();
                    var left = stack.Count == 0 ? 0 : stack.Peek() + 1;
                    var width = x - left;
                    if (heights[bar] == 0 || width == 0) continue;
                    var candidate = new HybridCropRectangle(left, y - heights[bar] + 1, width, heights[bar]);
                    if (IsBetter(candidate, best)) best = candidate;
                }
                if (x < size.Width) stack.Push(x);
            }
        }
        return best ?? throw new InvalidOperationException("A 与变换后 B 没有可双线性采样的有效交集。");
    }

    public double ValidateUsable(HybridCropRectangle crop, ImageSize referenceSize)
    {
        if (!crop.IsInside(referenceSize)) throw new ArgumentException("裁切矩形超出 A 图边界。", nameof(crop));
        var coverage = crop.Area / (double)referenceSize.PixelCount;
        if (crop.Width < 32 || crop.Height < 32 || coverage < 0.1d)
            throw new InvalidOperationException("有效交集不足：边长必须至少 32 像素且覆盖 A 的 10%。");
        return coverage;
    }

    public void ValidateUserCrop(HybridCropRectangle userCrop, HybridCropRectangle maximumCrop)
    {
        if (userCrop.X < maximumCrop.X || userCrop.Y < maximumCrop.Y ||
            userCrop.Right > maximumCrop.Right || userCrop.Bottom > maximumCrop.Bottom)
            throw new ArgumentException("用户裁切只能在默认有效矩形内收紧。", nameof(userCrop));
    }

    public HybridLumaPlane Crop(HybridLumaPlane source, HybridCropRectangle crop,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!crop.IsInside(source.Size)) throw new ArgumentException("裁切矩形超出亮度平面。", nameof(crop));
        var output = new double[checked((int)crop.Area)];
        for (var y = 0; y < crop.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            source.Values.Span.Slice(((crop.Y + y) * source.Size.Width) + crop.X, crop.Width)
                .CopyTo(output.AsSpan(y * crop.Width, crop.Width));
        }
        return new HybridLumaPlane(crop.Size, output);
    }

    private static bool IsBetter(HybridCropRectangle candidate, HybridCropRectangle? current)
    {
        if (current is null) return true;
        var value = current.Value;
        if (candidate.Area != value.Area) return candidate.Area > value.Area;
        if (candidate.Y != value.Y) return candidate.Y < value.Y;
        if (candidate.X != value.X) return candidate.X < value.X;
        if (candidate.Height != value.Height) return candidate.Height < value.Height;
        return candidate.Width < value.Width;
    }
}
