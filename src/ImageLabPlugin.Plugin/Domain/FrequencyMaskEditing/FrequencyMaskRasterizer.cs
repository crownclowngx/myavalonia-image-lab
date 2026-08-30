using ImageLabPlugin.Domain.Frequency;

namespace ImageLabPlugin.Domain.FrequencyMaskEditing;

/// <summary>把归一化操作配方确定性重放为自然索引下的共轭对称增益网格。</summary>
internal sealed class FrequencyMaskRasterizer(ConjugateMaskWriter writer)
{
    public FrequencyGainMask Rasterize(FrequencyMaskRecipe recipe, int width, int height,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        if (width <= 0 || height <= 0 || width > 2048 || height > 2048 ||
            checked(width * height) > FrequencySpectrum.MaximumComplexValues)
            throw new ArgumentOutOfRangeException(nameof(width), "遮罩网格超出共享 FFT 预算。");
        var gains = Enumerable.Repeat(1d, checked(width * height)).ToArray();
        foreach (var operation in recipe.OperationSpan)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Apply(gains, width, height, operation, cancellationToken);
        }
        return new FrequencyGainMask(width, height, gains, $"edit-{recipe.Fingerprint()}-{width}x{height}");
    }

    public FrequencyGainMask CreateEffective(FrequencyGainMask editMask, double strength,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(editMask);
        if (!double.IsFinite(strength) || strength is < 0d or > 1d) throw new ArgumentOutOfRangeException(nameof(strength));
        var source = editMask.GainSpan;
        var effective = new double[source.Length];
        for (var i = 0; i < effective.Length; i++)
        {
            if ((i & 16383) == 0) cancellationToken.ThrowIfCancellationRequested();
            // 强度只产生执行遮罩，不改写操作历史；s=0 必须逐值全通，s=1 必须逐值等于编辑遮罩。
            effective[i] = 1d - strength + (strength * source[i]);
        }
        return new FrequencyGainMask(editMask.Width, editMask.Height, effective,
            $"effective-{editMask.Fingerprint}-{strength:R}");
    }

    private void Apply(double[] gains, int width, int height, FrequencyMaskOperation operation, CancellationToken token)
    {
        switch (operation.Kind)
        {
            case FrequencyMaskOperationKind.BrushStroke:
            case FrequencyMaskOperationKind.EraseStroke:
                ApplyStroke(gains, width, height, operation, token);
                break;
            case FrequencyMaskOperationKind.RectangleFill:
                ApplyGeometry(gains, width, height, operation, static (x, y, item) =>
                    x >= Math.Min(item.Start.X, item.End.X) && x <= Math.Max(item.Start.X, item.End.X) &&
                    y >= Math.Min(item.Start.Y, item.End.Y) && y <= Math.Max(item.Start.Y, item.End.Y), token);
                break;
            case FrequencyMaskOperationKind.RingFill:
                ApplyGeometry(gains, width, height, operation, static (x, y, item) =>
                {
                    var distance = Math.Sqrt(Math.Pow(x - item.Start.X, 2d) + Math.Pow(y - item.Start.Y, 2d));
                    return distance >= item.InnerRadius && distance <= item.OuterRadius;
                }, token);
                break;
            case FrequencyMaskOperationKind.InvertAll:
                for (var i = 0; i < gains.Length; i++)
                {
                    if ((i & 16383) == 0) token.ThrowIfCancellationRequested();
                    gains[i] = 1d - gains[i];
                }
                break;
            case FrequencyMaskOperationKind.ResetAllPass:
                Array.Fill(gains, 1d);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation), "未知遮罩操作。");
        }
    }

    private void ApplyStroke(double[] gains, int width, int height, FrequencyMaskOperation operation, CancellationToken token)
    {
        // 整个 gesture 先形成命中集合；同一 bin 不会因 Pointer 事件更密或插值 stamp 重叠而重复混合。
        var hits = new bool[gains.Length];
        var points = operation.PointSpan;
        for (var i = 0; i < points.Length; i++)
        {
            if (i == 0) MarkCircle(hits, width, height, points[i], operation.Radius, token);
            if (i == 0) continue;
            var previous = points[i - 1];
            var current = points[i];
            var distance = Math.Sqrt(Math.Pow(current.X - previous.X, 2d) + Math.Pow(current.Y - previous.Y, 2d));
            var spacing = Math.Max(operation.Radius * 0.5d, 1d / Math.Max(width, height));
            var steps = Math.Max(1, (int)Math.Ceiling(distance / spacing));
            for (var step = 1; step <= steps; step++)
            {
                var t = step / (double)steps;
                MarkCircle(hits, width, height,
                    new NormalizedFrequencyPoint(previous.X + ((current.X - previous.X) * t), previous.Y + ((current.Y - previous.Y) * t)),
                    operation.Radius, token);
            }
        }
        CommitHits(gains, width, height, hits, operation, token);
    }

    private static void MarkCircle(bool[] hits, int width, int height, NormalizedFrequencyPoint center,
        double radius, CancellationToken token)
    {
        for (var displayY = 0; displayY < height; displayY++)
        {
            token.ThrowIfCancellationRequested();
            var y = height == 1 ? 0d : displayY / (double)(height - 1);
            for (var displayX = 0; displayX < width; displayX++)
            {
                var x = width == 1 ? 0d : displayX / (double)(width - 1);
                if (Math.Pow(x - center.X, 2d) + Math.Pow(y - center.Y, 2d) > radius * radius) continue;
                var point = FrequencyCoordinates.FromDisplay(displayX, displayY, width, height);
                hits[(point.InternalY * width) + point.InternalX] = true;
            }
        }
    }

    private void ApplyGeometry(double[] gains, int width, int height, FrequencyMaskOperation operation,
        Func<double, double, FrequencyMaskOperation, bool> contains, CancellationToken token)
    {
        var hits = new bool[gains.Length];
        for (var displayY = 0; displayY < height; displayY++)
        {
            token.ThrowIfCancellationRequested();
            var y = height == 1 ? 0d : displayY / (double)(height - 1);
            for (var displayX = 0; displayX < width; displayX++)
            {
                var x = width == 1 ? 0d : displayX / (double)(width - 1);
                if (!contains(x, y, operation)) continue;
                var point = FrequencyCoordinates.FromDisplay(displayX, displayY, width, height);
                hits[(point.InternalY * width) + point.InternalX] = true;
            }
        }
        CommitHits(gains, width, height, hits, operation, token);
    }

    private void CommitHits(double[] gains, int width, int height, bool[] hits, FrequencyMaskOperation operation,
        CancellationToken token)
    {
        for (var index = 0; index < hits.Length; index++)
        {
            if ((index & 16383) == 0) token.ThrowIfCancellationRequested();
            if (!hits[index]) continue;
            var x = index % width;
            var y = index / width;
            var conjugate = FrequencyCoordinates.ConjugateIndex(x, y, width, height);
            var pairedIndex = (conjugate.Y * width) + conjugate.X;
            // 只由较小线性索引提交一次，防止几何同时命中共轭两侧时 opacity 被重复应用。
            if (hits[pairedIndex] && pairedIndex < index) continue;
            writer.Mix(gains, width, height, x, y, operation.TargetGain, operation.Opacity, operation.BandLock);
        }
    }
}
