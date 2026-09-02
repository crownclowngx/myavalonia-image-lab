using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Domain.SeamCarving;

/// <summary>以确定性动态规划寻找垂直或水平最小累计有效能量缝。</summary>
/// <remarks>
/// “主轴”是路径前进方向：垂直缝主轴为 y、次轴为 x；水平缝主轴为 x、次轴为 y。
/// 循环通过坐标映射复用，但不转置整图。每个单元保存累计 double 与指向上一主轴位置的 sbyte 偏移；
/// 前驱或终点代价严格相等时选择较小次轴坐标，从而使所有机器和重复运行得到相同路径。
/// </remarks>
internal sealed class MinimumEnergySeamFinder
{
    public SeamPath Find(SeamEnergyMap energy, SeamMask mask, SeamOrientation orientation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(energy);
        ArgumentNullException.ThrowIfNull(mask);
        if (energy.Size != mask.Size) throw new ArgumentException("能量与蒙版尺寸必须一致。", nameof(mask));
        var mainLength = orientation == SeamOrientation.Vertical ? energy.Size.Height : energy.Size.Width;
        var secondaryLength = orientation == SeamOrientation.Vertical ? energy.Size.Width : energy.Size.Height;
        var cellCount = checked(mainLength * secondaryLength);
        var cumulative = new double[cellCount];
        var predecessor = new sbyte[cellCount];

        for (var secondary = 0; secondary < secondaryLength; secondary++)
            cumulative[secondary] = GetEffective(energy, orientation, 0, secondary);

        for (var main = 1; main < mainLength; main++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = main * secondaryLength;
            var previousRow = row - secondaryLength;
            for (var secondary = 0; secondary < secondaryLength; secondary++)
            {
                var first = Math.Max(0, secondary - 1);
                var last = Math.Min(secondaryLength - 1, secondary + 1);
                var best = first;
                var bestCost = cumulative[previousRow + first];
                for (var candidate = first + 1; candidate <= last; candidate++)
                {
                    var candidateCost = cumulative[previousRow + candidate];
                    if (candidateCost < bestCost)
                    {
                        best = candidate;
                        bestCost = candidateCost;
                    }
                }
                var cost = bestCost + GetEffective(energy, orientation, main, secondary);
                if (!double.IsFinite(cost))
                    throw new InvalidOperationException($"动态规划在主轴 {main}、次轴 {secondary} 产生非有限累计代价。");
                cumulative[row + secondary] = cost;
                predecessor[row + secondary] = checked((sbyte)(best - secondary));
            }
        }

        var lastRow = (mainLength - 1) * secondaryLength;
        var end = 0;
        for (var secondary = 1; secondary < secondaryLength; secondary++)
            if (cumulative[lastRow + secondary] < cumulative[lastRow + end]) end = secondary;

        var coordinates = new int[mainLength];
        coordinates[^1] = end;
        for (var main = mainLength - 1; main > 0; main--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = coordinates[main];
            coordinates[main - 1] = current + predecessor[(main * secondaryLength) + current];
        }

        double baseTotal = 0d;
        var protectHits = 0;
        var removalHits = 0;
        for (var main = 0; main < mainLength; main++)
        {
            var secondary = coordinates[main];
            var (x, y) = ToImageCoordinates(orientation, main, secondary);
            baseTotal += energy.GetBase(x, y);
            var maskValue = mask.Get(x, y);
            if (maskValue == SeamMaskValue.Protect) protectHits++;
            else if (maskValue == SeamMaskValue.PreferRemoval) removalHits++;
        }
        return new SeamPath(orientation, energy.Size, coordinates, baseTotal,
            cumulative[lastRow + end], protectHits, removalHits);
    }

    private static double GetEffective(SeamEnergyMap energy, SeamOrientation orientation, int main, int secondary)
    {
        var (x, y) = ToImageCoordinates(orientation, main, secondary);
        return energy.GetEffective(x, y);
    }

    private static (int X, int Y) ToImageCoordinates(SeamOrientation orientation, int main, int secondary) =>
        orientation == SeamOrientation.Vertical ? (secondary, main) : (main, secondary);
}

/// <summary>使用同一条已验证路径同步删除 RGBA 和三态蒙版。</summary>
internal sealed class SeamRemover
{
    public (PixelImage Image, SeamMask Mask) Remove(PixelImage source, SeamMask mask, SeamPath path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(mask);
        ArgumentNullException.ThrowIfNull(path);
        if (source.Size != mask.Size || source.Size != path.SourceSize)
            throw new InvalidOperationException("删除路径不属于当前图片尺寸，禁止把过期路径套用到新帧。");
        return path.Orientation == SeamOrientation.Vertical
            ? RemoveVertical(source, mask, path.Coordinates.Span, cancellationToken)
            : RemoveHorizontal(source, mask, path.Coordinates.Span, cancellationToken);
    }

    private static (PixelImage, SeamMask) RemoveVertical(PixelImage source, SeamMask mask,
        ReadOnlySpan<int> coordinates, CancellationToken cancellationToken)
    {
        if (source.Size.Width == 1) throw new InvalidOperationException("宽度已为 1，不能继续删除垂直缝。");
        var targetSize = new ImageSize(source.Size.Width - 1, source.Size.Height);
        var rgba = new byte[checked((int)targetSize.PixelCount * 4)];
        var maskBytes = new byte[checked((int)targetSize.PixelCount)];
        var sourceRgba = source.Rgba.Span;
        var sourceMask = mask.Values.Span;
        for (var y = 0; y < source.Size.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var seam = coordinates[y];
            var sourceRow = y * source.Size.Width;
            var targetRow = y * targetSize.Width;
            sourceRgba.Slice(sourceRow * 4, seam * 4).CopyTo(rgba.AsSpan(targetRow * 4));
            sourceRgba.Slice((sourceRow + seam + 1) * 4, (source.Size.Width - seam - 1) * 4)
                .CopyTo(rgba.AsSpan((targetRow + seam) * 4));
            sourceMask.Slice(sourceRow, seam).CopyTo(maskBytes.AsSpan(targetRow));
            sourceMask.Slice(sourceRow + seam + 1, source.Size.Width - seam - 1)
                .CopyTo(maskBytes.AsSpan(targetRow + seam));
        }
        return (new PixelImage(targetSize, rgba), new SeamMask(targetSize, maskBytes));
    }

    private static (PixelImage, SeamMask) RemoveHorizontal(PixelImage source, SeamMask mask,
        ReadOnlySpan<int> coordinates, CancellationToken cancellationToken)
    {
        if (source.Size.Height == 1) throw new InvalidOperationException("高度已为 1，不能继续删除水平缝。");
        var targetSize = new ImageSize(source.Size.Width, source.Size.Height - 1);
        var rgba = new byte[checked((int)targetSize.PixelCount * 4)];
        var maskBytes = new byte[checked((int)targetSize.PixelCount)];
        var sourceRgba = source.Rgba.Span;
        var sourceMask = mask.Values.Span;
        for (var x = 0; x < source.Size.Width; x++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var seam = coordinates[x];
            for (var y = 0; y < source.Size.Height; y++)
            {
                if (y == seam) continue;
                var targetY = y < seam ? y : y - 1;
                var sourceIndex = (y * source.Size.Width) + x;
                var targetIndex = (targetY * targetSize.Width) + x;
                sourceRgba.Slice(sourceIndex * 4, 4).CopyTo(rgba.AsSpan(targetIndex * 4, 4));
                maskBytes[targetIndex] = sourceMask[sourceIndex];
            }
        }
        return (new PixelImage(targetSize, rgba), new SeamMask(targetSize, maskBytes));
    }
}
