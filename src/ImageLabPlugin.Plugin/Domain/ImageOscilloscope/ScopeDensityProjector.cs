namespace ImageLabPlugin.Domain.ImageOscilloscope;

/// <summary>把精确计数投影为线性或 log1p 显示强度，不重新读取源像素。</summary>
internal sealed class ScopeDensityProjector
{
    public ScopeDensityProjection Project(ScopeCountGrid grid, ScopeDensityMode mode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grid);
        var upper = PercentileUpper(grid.Span, cancellationToken);
        return ProjectWithUpper(grid, mode, upper, cancellationToken);
    }

    public (ScopeDensityProjection Red, ScopeDensityProjection Green, ScopeDensityProjection Blue) ProjectParade(
        ScopeCountGrid red, ScopeCountGrid green, ScopeCountGrid blue, ScopeDensityMode mode,
        CancellationToken cancellationToken = default)
    {
        EnsureSameSize(red, green, blue);
        var combined = new uint[checked(red.Span.Length + green.Span.Length + blue.Span.Length)];
        red.Span.CopyTo(combined); green.Span.CopyTo(combined.AsSpan(red.Span.Length));
        blue.Span.CopyTo(combined.AsSpan(red.Span.Length + green.Span.Length));
        var upper = PercentileUpper(combined, cancellationToken);
        return (ProjectWithUpper(red, mode, upper, cancellationToken),
            ProjectWithUpper(green, mode, upper, cancellationToken),
            ProjectWithUpper(blue, mode, upper, cancellationToken));
    }

    internal static uint PercentileUpper(ReadOnlySpan<uint> counts, CancellationToken cancellationToken = default)
    {
        var nonZero = new List<uint>();
        for (var index = 0; index < counts.Length; index++)
        {
            if ((index & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (counts[index] > 0) nonZero.Add(counts[index]);
        }
        if (nonZero.Count == 0) return 1;
        nonZero.Sort();
        cancellationToken.ThrowIfCancellationRequested();
        var rank = Math.Max(1, (int)Math.Ceiling(0.995d * nonZero.Count));
        return Math.Max(1u, nonZero[rank - 1]);
    }

    private static ScopeDensityProjection ProjectWithUpper(ScopeCountGrid grid, ScopeDensityMode mode, uint upper,
        CancellationToken cancellationToken)
    {
        var tones = new float[grid.Span.Length];
        var denominator = mode == ScopeDensityMode.Logarithmic ? Math.Log(1d + upper) : upper;
        for (var index = 0; index < tones.Length; index++)
        {
            if ((index & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            var count = grid.Span[index];
            var value = count == 0 ? 0d : mode == ScopeDensityMode.Logarithmic
                ? Math.Log(1d + count) / denominator : count / denominator;
            tones[index] = (float)Math.Clamp(value, 0d, 1d);
        }
        return new ScopeDensityProjection(grid.Width, grid.Height, upper, tones);
    }

    private static void EnsureSameSize(params ScopeCountGrid[] grids)
    {
        ArgumentNullException.ThrowIfNull(grids);
        if (grids.Length == 0 || grids.Any(grid => grid.Width != grids[0].Width || grid.Height != grids[0].Height))
            throw new ArgumentException("RGB Parade 三个栅格必须同尺寸。", nameof(grids));
    }
}
