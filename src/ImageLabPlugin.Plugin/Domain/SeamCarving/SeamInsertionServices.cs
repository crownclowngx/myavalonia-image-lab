using System.Text;
using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Domain.SeamCarving;

/// <summary>在影子副本上“寻找→删除”，把一批互不重复的缝映射回批次起点坐标。</summary>
/// <remarks>
/// 直接在已放大的真实图上继续找缝会反复命中新插入的低能量副本。这里让规划副本只缩不放，
/// 并为每行/列维护“当前位置→批次起点坐标”表。规划结果只保存坐标，不保存影子 RGBA 帧。
/// </remarks>
internal sealed class SeamInsertionPlanner(
    SobelEnergyCalculator energyCalculator,
    MinimumEnergySeamFinder seamFinder,
    SeamRemover remover)
{
    public SeamInsertionBatch Plan(PixelImage source, SeamMask mask, SeamOrientation orientation, int count,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(mask);
        if (source.Size != mask.Size) throw new ArgumentException("图片与蒙版尺寸必须一致。", nameof(mask));
        var secondaryLength = orientation == SeamOrientation.Vertical ? source.Size.Width : source.Size.Height;
        if (count <= 0 || count > secondaryLength - 1)
            throw new ArgumentOutOfRangeException(nameof(count), $"单批插入数必须位于 1 至 {secondaryLength - 1}。");

        var mainLength = orientation == SeamOrientation.Vertical ? source.Size.Height : source.Size.Width;
        var coordinateMaps = new int[mainLength][];
        for (var main = 0; main < mainLength; main++)
            coordinateMaps[main] = Enumerable.Range(0, secondaryLength).ToArray();

        var shadowImage = source.Clone();
        var shadowMask = mask.Clone();
        var paths = new List<SeamInsertionPath>(count);
        for (var pathIndex = 0; pathIndex < count; pathIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var energy = energyCalculator.Calculate(shadowImage, shadowMask, cancellationToken);
            var path = seamFinder.Find(energy, shadowMask, orientation, cancellationToken);
            var mapped = new int[mainLength];
            for (var main = 0; main < mainLength; main++)
                mapped[main] = coordinateMaps[main][path.Coordinates.Span[main]];
            paths.Add(new SeamInsertionPath(orientation, source.Size, mapped));

            // 与影子 RGBA/蒙版删除同一个次轴坐标，保证下次规划不会再次选择同一源像素。
            for (var main = 0; main < mainLength; main++)
            {
                var removeAt = path.Coordinates.Span[main];
                var old = coordinateMaps[main];
                var next = new int[old.Length - 1];
                old.AsSpan(0, removeAt).CopyTo(next);
                old.AsSpan(removeAt + 1).CopyTo(next.AsSpan(removeAt));
                coordinateMaps[main] = next;
            }
            (shadowImage, shadowMask) = remover.Remove(shadowImage, shadowMask, path, cancellationToken);
        }

        var identity = new StringBuilder($"{orientation}|{source.Size.Width}x{source.Size.Height}|");
        foreach (var path in paths)
        {
            foreach (var coordinate in path.OriginalCoordinates) identity.Append(coordinate).Append(',');
            identity.Append(';');
        }
        return new SeamInsertionBatch(orientation, source.Size, paths, SeamFingerprint.ForText(identity.ToString()));
    }
}

/// <summary>把一条已规划源坐标缝插入真实工作图，并同步传播三态蒙版。</summary>
/// <remarks>
/// 新像素位于垂直缝右侧或水平缝下方。颜色先转成预乘 sRGB 再平均 Alpha 与预乘颜色，最后反预乘；
/// 这样透明边缘不会把隐藏 RGB 扩散成彩边。两端都全透明时固定写 (0,0,0,0)，字节使用 ToEven 舍入。
/// </remarks>
internal sealed class SeamInserter
{
    public (PixelImage Image, SeamMask Mask, int[] AppliedCoordinates) Insert(
        PixelImage source,
        SeamMask mask,
        SeamInsertionPath plannedPath,
        IReadOnlyList<SeamInsertionPath> previouslyApplied,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(mask);
        ArgumentNullException.ThrowIfNull(plannedPath);
        if (source.Size != mask.Size) throw new ArgumentException("图片与蒙版尺寸必须一致。", nameof(mask));
        if (previouslyApplied.Any(path => path.Orientation != plannedPath.Orientation ||
                path.BatchSourceSize != plannedPath.BatchSourceSize))
            throw new InvalidOperationException("插入偏移只能由同一批次、同一方向的先前路径提供。");
        var mainLength = plannedPath.Orientation == SeamOrientation.Vertical
            ? plannedPath.BatchSourceSize.Height : plannedPath.BatchSourceSize.Width;
        if (plannedPath.OriginalCoordinates.Count != mainLength)
            throw new ArgumentException("插入路径长度与批次主轴不一致。", nameof(plannedPath));

        var adjusted = AdjustCoordinates(plannedPath, previouslyApplied);
        return plannedPath.Orientation == SeamOrientation.Vertical
            ? InsertVertical(source, mask, adjusted, cancellationToken)
            : InsertHorizontal(source, mask, adjusted, cancellationToken);
    }

    internal static int[] AdjustCoordinates(SeamInsertionPath plannedPath,
        IReadOnlyList<SeamInsertionPath> previouslyApplied)
    {
        var adjusted = new int[plannedPath.OriginalCoordinates.Count];
        for (var main = 0; main < adjusted.Length; main++)
        {
            var original = plannedPath.OriginalCoordinates[main];
            var offset = 0;
            foreach (var previous in previouslyApplied)
                if (previous.OriginalCoordinates[main] <= original) offset++;
            adjusted[main] = checked(original + offset);
        }
        return adjusted;
    }

    private static (PixelImage, SeamMask, int[]) InsertVertical(PixelImage source, SeamMask mask,
        int[] coordinates, CancellationToken cancellationToken)
    {
        var targetSize = new ImageSize(source.Size.Width + 1, source.Size.Height);
        var rgba = new byte[checked((int)targetSize.PixelCount * 4)];
        var maskBytes = new byte[checked((int)targetSize.PixelCount)];
        var sourceRgba = source.Rgba.Span;
        var sourceMask = mask.Values.Span;
        for (var y = 0; y < source.Size.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var seam = coordinates[y];
            if ((uint)seam >= (uint)source.Size.Width) throw new InvalidOperationException("修正后的垂直插入坐标越界。");
            var sourceRow = y * source.Size.Width;
            var targetRow = y * targetSize.Width;
            sourceRgba.Slice(sourceRow * 4, (seam + 1) * 4).CopyTo(rgba.AsSpan(targetRow * 4));
            sourceMask.Slice(sourceRow, seam + 1).CopyTo(maskBytes.AsSpan(targetRow));
            var neighbor = seam == source.Size.Width - 1 ? Math.Max(0, seam - 1) : seam + 1;
            WriteInterpolated(sourceRgba, sourceRow + seam, sourceRow + neighbor, rgba, targetRow + seam + 1);
            maskBytes[targetRow + seam + 1] = MergeMask(sourceMask[sourceRow + seam], sourceMask[sourceRow + neighbor]);
            sourceRgba.Slice((sourceRow + seam + 1) * 4, (source.Size.Width - seam - 1) * 4)
                .CopyTo(rgba.AsSpan((targetRow + seam + 2) * 4));
            sourceMask.Slice(sourceRow + seam + 1, source.Size.Width - seam - 1)
                .CopyTo(maskBytes.AsSpan(targetRow + seam + 2));
        }
        return (new PixelImage(targetSize, rgba), new SeamMask(targetSize, maskBytes), coordinates);
    }

    private static (PixelImage, SeamMask, int[]) InsertHorizontal(PixelImage source, SeamMask mask,
        int[] coordinates, CancellationToken cancellationToken)
    {
        var targetSize = new ImageSize(source.Size.Width, source.Size.Height + 1);
        var rgba = new byte[checked((int)targetSize.PixelCount * 4)];
        var maskBytes = new byte[checked((int)targetSize.PixelCount)];
        var sourceRgba = source.Rgba.Span;
        var sourceMask = mask.Values.Span;
        for (var x = 0; x < source.Size.Width; x++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var seam = coordinates[x];
            if ((uint)seam >= (uint)source.Size.Height) throw new InvalidOperationException("修正后的水平插入坐标越界。");
            for (var targetY = 0; targetY < targetSize.Height; targetY++)
            {
                var targetIndex = (targetY * targetSize.Width) + x;
                if (targetY == seam + 1)
                {
                    var neighborY = seam == source.Size.Height - 1 ? Math.Max(0, seam - 1) : seam + 1;
                    var first = (seam * source.Size.Width) + x;
                    var second = (neighborY * source.Size.Width) + x;
                    WriteInterpolated(sourceRgba, first, second, rgba, targetIndex);
                    maskBytes[targetIndex] = MergeMask(sourceMask[first], sourceMask[second]);
                }
                else
                {
                    var sourceY = targetY <= seam ? targetY : targetY - 1;
                    var sourceIndex = (sourceY * source.Size.Width) + x;
                    sourceRgba.Slice(sourceIndex * 4, 4).CopyTo(rgba.AsSpan(targetIndex * 4, 4));
                    maskBytes[targetIndex] = sourceMask[sourceIndex];
                }
            }
        }
        return (new PixelImage(targetSize, rgba), new SeamMask(targetSize, maskBytes), coordinates);
    }

    private static byte MergeMask(byte first, byte second)
    {
        if (first == (byte)SeamMaskValue.Protect || second == (byte)SeamMaskValue.Protect)
            return (byte)SeamMaskValue.Protect;
        if (first == (byte)SeamMaskValue.PreferRemoval || second == (byte)SeamMaskValue.PreferRemoval)
            return (byte)SeamMaskValue.PreferRemoval;
        return (byte)SeamMaskValue.Normal;
    }

    internal static void WriteInterpolated(ReadOnlySpan<byte> source, int firstPixel, int secondPixel,
        Span<byte> target, int targetPixel)
    {
        var first = firstPixel * 4;
        var second = secondPixel * 4;
        var output = targetPixel * 4;
        var alpha1 = source[first + 3] / 255d;
        var alpha2 = source[second + 3] / 255d;
        var alpha = (alpha1 + alpha2) * 0.5d;
        if (alpha == 0d)
        {
            target.Slice(output, 4).Clear();
            return;
        }
        for (var channel = 0; channel < 3; channel++)
        {
            var premultiplied = ((source[first + channel] / 255d * alpha1) +
                (source[second + channel] / 255d * alpha2)) * 0.5d;
            target[output + channel] = ToByte(premultiplied / alpha * 255d);
        }
        target[output + 3] = ToByte(alpha * 255d);
    }

    internal static byte ToByte(double value) =>
        (byte)Math.Clamp(Math.Round(value, MidpointRounding.ToEven), 0d, 255d);
}
