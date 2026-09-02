using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Domain.PoissonBlending;

/// <summary>
/// 从已通过 placement/预算验证的输入构造紧凑离散方程。每个未知的方程为
/// <c>4 f_p - Σinternal f_q = Σguidance(p,q) + Σboundary target_q</c>；邻边固定按左、右、上、下累加。
/// 本类不显式构造 N×N 矩阵，只保存每个未知的四个邻接索引，从而让内存与 unknown 线性相关。
/// </summary>
internal sealed class PoissonProblemBuilder(
    SrgbColorSpace colorSpace,
    PoissonGuidanceCatalog guidanceCatalog,
    PoissonPlacementValidator placementValidator,
    PoissonMaskTopologyAnalyzer topologyAnalyzer,
    PoissonResourceEstimator resourceEstimator)
{
    private static readonly (int X, int Y)[] Directions = [(-1, 0), (1, 0), (0, -1), (0, 1)];

    public PoissonProblem Build(PixelImage source, PixelImage target, PoissonBinaryMask mask,
        ImageOffset offset, PoissonBlendOptions options, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(mask);
        options.Validate();
        var validation = placementValidator.Validate(source, target, mask, offset, token);
        if (!validation.IsValid) throw new InvalidOperationException(string.Join("；", validation.Issues.Select(item => item.Message).Distinct()));
        var topology = topologyAnalyzer.Analyze(mask, token);
        var strategy = guidanceCatalog.Resolve(options.Mode);
        var estimate = resourceEstimator.Estimate(source.Size, target.Size, topology, strategy.ChannelCount, options.MaxIterations);
        if (!estimate.IsAllowed) throw new InvalidOperationException(string.Join("；", estimate.BlockingReasons));

        var count = topology.UnknownCount;
        var sourceX = new int[count]; var sourceY = new int[count]; var targetX = new int[count]; var targetY = new int[count];
        // unknownIndex 只覆盖遮罩闭开包围盒，避免“16 MP 源图 + 很小遮罩”仍分配整图 int 索引。
        // halo 邻居先查 mask；只有域内邻居才换算为 box 局部索引。
        var box = topology.BoundingBox;
        var indexByBox = Enumerable.Repeat(-1, checked(box.Width * box.Height)).ToArray();
        var cursor = 0;
        // source y/x 与纯平移后的 target y/x 顺序一致；固定顺序是跨运行确定性的组成部分。
        for (var sy = 0; sy < source.Size.Height; sy++) for (var sx = 0; sx < source.Size.Width; sx++)
        {
            if (!mask.Contains(sx, sy)) continue;
            sourceX[cursor] = sx; sourceY[cursor] = sy; targetX[cursor] = sx + offset.Dx; targetY[cursor] = sy + offset.Dy;
            indexByBox[((sy - box.Top) * box.Width) + (sx - box.Left)] = cursor++;
        }

        var neighbors = Enumerable.Repeat(-1, checked(count * 4)).ToArray();
        var rhs = new double[checked(count * strategy.ChannelCount)];
        var initial = new double[rhs.Length]; long sourceEdges = 0; long targetEdges = 0;
        for (var i = 0; i < count; i++)
        {
            if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
            var sx = sourceX[i]; var sy = sourceY[i]; var tx = targetX[i]; var ty = targetY[i];
            var sourceP = Decode(source, sx, sy); var targetP = Decode(target, tx, ty);
            for (var channel = 0; channel < strategy.ChannelCount; channel++)
                initial[(i * strategy.ChannelCount) + channel] = strategy.ChannelCount == 1 ? MonochromeGuidanceStrategy.Luma(targetP) : Channel(targetP, channel);
            for (var directionIndex = 0; directionIndex < Directions.Length; directionIndex++)
            {
                var direction = Directions[directionIndex]; var nsx = sx + direction.X; var nsy = sy + direction.Y;
                var ntx = tx + direction.X; var nty = ty + direction.Y;
                var neighborIndex = mask.Contains(nsx, nsy)
                    ? indexByBox[((nsy - box.Top) * box.Width) + (nsx - box.Left)] : -1;
                neighbors[(i * 4) + directionIndex] = neighborIndex;
                var sourceQ = Decode(source, nsx, nsy); var targetQ = Decode(target, ntx, nty);
                var guidance = strategy.Evaluate(sourceP, sourceQ, targetP, targetQ);
                if (!guidance.IsFinite) throw new InvalidOperationException("guidance 出现非有限数。 ");
                if (guidance.SelectedSource) sourceEdges++; else targetEdges++;
                for (var channel = 0; channel < strategy.ChannelCount; channel++)
                {
                    var flat = (i * strategy.ChannelCount) + channel;
                    rhs[flat] += guidance.Get(channel);
                    if (neighborIndex < 0)
                        rhs[flat] += strategy.ChannelCount == 1 ? MonochromeGuidanceStrategy.Luma(targetQ) : Channel(targetQ, channel);
                }
            }
        }
        return new PoissonProblem(PoissonFingerprint.ForProblem(source, target, mask, offset, options.Mode), options.Mode,
            target.Size, sourceX, sourceY, targetX, targetY, neighbors, rhs, initial, topology, estimate, sourceEdges, targetEdges);
    }

    private LinearRgbColor Decode(PixelImage image, int x, int y)
    { var pixel = image.GetPixel(x, y); return colorSpace.Decode(SrgbColor.FromBytes(pixel.R, pixel.G, pixel.B)); }
    internal static double Channel(LinearRgbColor color, int channel) => channel switch
    { 0 => color.Red, 1 => color.Green, 2 => color.Blue, _ => throw new ArgumentOutOfRangeException(nameof(channel)) };
}

/// <summary>在任何大数组或后台任务开始前，以 checked long 估算工作量和同时存活的主要对象。</summary>
internal sealed class PoissonResourceEstimator
{
    public const int MaximumUnknowns = 500_000;
    public const long MaximumBoundingBoxPixels = 1_000_000;
    public const long MaximumScalarUpdates = 180_000_000;
    public const long MaximumPeakBytes = 512L * 1024 * 1024;

    public PoissonResourceEstimate Estimate(ImageSize source, ImageSize target, PoissonMaskTopology topology,
        int channelCount, int maximumIterations)
    {
        if (channelCount is not 1 and not 3) throw new ArgumentOutOfRangeException(nameof(channelCount));
        var boxPixels = checked((long)topology.BoundingBox.Width * topology.BoundingBox.Height);
        var updates = checked((long)topology.UnknownCount * channelCount * maximumIterations);
        // 两张输入、Alpha/Poisson/残差代理、box mask/index、四组坐标、四邻接、问题初值/RHS、当前解与
        // sweep 事务备份都按同时存活计入，再加 35% 安全余量；不能用 GC 可能回收来缩小门禁。
        var baseBytes = checked((source.PixelCount * 4L) + (target.PixelCount * 16L) + boxPixels + (boxPixels * 4L) +
            ((long)topology.UnknownCount * 32L) + ((long)topology.UnknownCount * channelCount * 32L) + (2_001L * 32L));
        var peak = checked(baseBytes + ((baseBytes * 35L) / 100L));
        var reasons = new List<string>();
        if (topology.UnknownCount > MaximumUnknowns) reasons.Add($"未知量 {topology.UnknownCount:N0} 超过 {MaximumUnknowns:N0}；请缩小遮罩。 ");
        if (boxPixels > MaximumBoundingBoxPixels) reasons.Add($"遮罩包围盒 {boxPixels:N0} 像素超过 {MaximumBoundingBoxPixels:N0}。 ");
        if (updates > MaximumScalarUpdates) reasons.Add($"标量更新 {updates:N0} 超过 {MaximumScalarUpdates:N0}；请缩小遮罩或显式降低最大迭代。 ");
        if (peak > MaximumPeakBytes) reasons.Add($"估算峰值 {peak / 1024d / 1024d:F1} MiB 超过 512 MiB。 ");
        return new(topology.UnknownCount, boxPixels, channelCount, updates, peak, reasons);
    }
}
