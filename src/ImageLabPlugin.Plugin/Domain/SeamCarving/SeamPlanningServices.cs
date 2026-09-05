using System.Globalization;
using System.Text;
using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Domain.SeamCarving;

/// <summary>以硬边圆笔刷确定性重放归一化笔划。</summary>
/// <remarks>
/// 相邻采样点用短线段插值，避免快速拖动留下空洞。后画笔划覆盖先画笔划，擦除写回 Normal；
/// 半径取短边比例，使同一快照在相同图片尺寸上重放完全一致。
/// </remarks>
internal sealed class SeamMaskRasterizer
{
    public const int MaximumStrokes = 512;

    public SeamMask Rasterize(ImageSize size, IReadOnlyList<SeamBrushStroke> strokes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(strokes);
        if (strokes.Count > MaximumStrokes)
            throw new ArgumentOutOfRangeException(nameof(strokes), $"笔划数 {strokes.Count} 超过 {MaximumStrokes} 上限。");
        var result = new SeamMask(size);
        foreach (var stroke in strokes.OrderBy(item => item.Sequence))
        {
            cancellationToken.ThrowIfCancellationRequested();
            stroke.Validate();
            DrawStroke(result, stroke, cancellationToken);
        }
        return result;
    }

    private static void DrawStroke(SeamMask mask, SeamBrushStroke stroke, CancellationToken cancellationToken)
    {
        var radius = Math.Max(1d, stroke.RadiusNormalized * Math.Min(mask.Size.Width, mask.Size.Height));
        var value = stroke.Tool switch
        {
            SeamBrushTool.Protect => SeamMaskValue.Protect,
            SeamBrushTool.PreferRemoval => SeamMaskValue.PreferRemoval,
            _ => SeamMaskValue.Normal
        };
        for (var segment = 0; segment < stroke.Points.Count; segment++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var start = stroke.Points[Math.Max(0, segment - 1)];
            var end = stroke.Points[segment];
            var startX = start.X * (mask.Size.Width - 1);
            var startY = start.Y * (mask.Size.Height - 1);
            var endX = end.X * (mask.Size.Width - 1);
            var endY = end.Y * (mask.Size.Height - 1);
            var distance = Math.Max(Math.Abs(endX - startX), Math.Abs(endY - startY));
            var samples = Math.Max(1, (int)Math.Ceiling(distance / Math.Max(1d, radius * 0.5d)));
            for (var sample = 0; sample <= samples; sample++)
            {
                var amount = sample / (double)samples;
                DrawCircle(mask, startX + ((endX - startX) * amount), startY + ((endY - startY) * amount), radius, value);
            }
        }
    }

    private static void DrawCircle(SeamMask mask, double centerX, double centerY, double radius, SeamMaskValue value)
    {
        var minimumX = Math.Max(0, (int)Math.Floor(centerX - radius));
        var maximumX = Math.Min(mask.Size.Width - 1, (int)Math.Ceiling(centerX + radius));
        var minimumY = Math.Max(0, (int)Math.Floor(centerY - radius));
        var maximumY = Math.Min(mask.Size.Height - 1, (int)Math.Ceiling(centerY + radius));
        var radiusSquared = radius * radius;
        for (var y = minimumY; y <= maximumY; y++)
            for (var x = minimumX; x <= maximumX; x++)
            {
                var deltaX = x - centerX;
                var deltaY = y - centerY;
                if ((deltaX * deltaX) + (deltaY * deltaY) <= radiusSquared) mask.Set(x, y, value);
            }
    }
}

/// <summary>在任何大数组分配或后台任务启动前估算 Seam 计划的工作量与峰值内存。</summary>
/// <remarks>
/// 峰值包含三份 RGBA、蒙版、亮度/能量/累计三个 double 平面、前驱、插入映射、路径坐标和预览，
/// 再加 25% 安全余量。该公式是可复现的拒绝门禁，不是运行时内存承诺。
/// </remarks>
internal sealed class SeamResourceEstimator
{
    public const long MaximumWorkingPixels = 2_000_000;
    public const int MaximumTotalSeams = 256;
    public const double MaximumAxisChangeRatio = 0.25d;
    public const long MaximumCellVisits = 160_000_000;
    public const long MaximumPlannedCoordinates = 8_000_000;

    public SeamResourceEstimate Estimate(ImageSize input, ImageSize target)
    {
        var widthDelta = target.Width - input.Width;
        var heightDelta = target.Height - input.Height;
        var totalSeams = checked(Math.Abs(widthDelta) + Math.Abs(heightDelta));
        var widthRatio = Math.Abs(widthDelta) / (double)input.Width;
        var heightRatio = Math.Abs(heightDelta) / (double)input.Height;
        var maximumPixels = Math.Max(input.PixelCount, target.PixelCount);
        var visits = EstimateCellVisits(input, target);
        var coordinates = Math.Max(
            checked((long)Math.Abs(widthDelta) * input.Height),
            checked((long)Math.Abs(heightDelta) * input.Width));
        var peakBytes = EstimatePeakBytes(maximumPixels, coordinates, widthDelta > 0 || heightDelta > 0);
        var reasons = new List<string>();
        if (maximumPixels > MaximumWorkingPixels)
            reasons.Add($"工作图像素 {maximumPixels:N0} / 上限 {MaximumWorkingPixels:N0}；请缩小输入或目标尺寸。");
        if (totalSeams > MaximumTotalSeams)
            reasons.Add($"总缝数 {totalSeams:N0} / 上限 {MaximumTotalSeams:N0}；请减小尺寸变化。");
        if (widthRatio > MaximumAxisChangeRatio)
            reasons.Add($"宽度变化 {widthRatio:P1} / 上限 {MaximumAxisChangeRatio:P0}；请分阶段处理。");
        if (heightRatio > MaximumAxisChangeRatio)
            reasons.Add($"高度变化 {heightRatio:P1} / 上限 {MaximumAxisChangeRatio:P0}；请分阶段处理。");
        if (input.Width == 1 && target.Width > input.Width)
            reasons.Add("输入宽度为 1，缺少水平插值邻居，不能插入垂直缝；请先使用普通缩放扩为至少 2 像素宽。");
        if (input.Height == 1 && target.Height > input.Height)
            reasons.Add("输入高度为 1，缺少垂直插值邻居，不能插入水平缝；请先使用普通缩放扩为至少 2 像素高。");
        if (visits > MaximumCellVisits)
            reasons.Add($"估算单元访问 {visits:N0} / 上限 {MaximumCellVisits:N0}；请减少像素或缝数。");
        if (coordinates > MaximumPlannedCoordinates)
            reasons.Add($"插入路径坐标 {coordinates:N0} / 上限 {MaximumPlannedCoordinates:N0}；请减少放大缝数。");
        return new(maximumPixels, totalSeams, widthRatio, heightRatio, visits, peakBytes, coordinates, reasons);
    }

    private static long EstimateCellVisits(ImageSize input, ImageSize target)
    {
        var widthSteps = Math.Abs(target.Width - input.Width);
        var heightSteps = Math.Abs(target.Height - input.Height);
        var widthSum = ArithmeticDimensionSum(input.Width, widthSteps, target.Width > input.Width);
        var heightSum = ArithmeticDimensionSum(input.Height, heightSteps, target.Height > input.Height);
        var widthVisits = checked(widthSum * input.Height);
        var heightVisits = checked(heightSum * target.Width);
        // 每个插入步骤还在影子副本执行一次寻找与删除；删除步骤无需重复计数。
        var insertionVisits = 0L;
        if (target.Width > input.Width) insertionVisits = checked(insertionVisits + widthVisits);
        if (target.Height > input.Height) insertionVisits = checked(insertionVisits + heightVisits);
        return checked(widthVisits + heightVisits + insertionVisits);
    }

    private static long ArithmeticDimensionSum(int start, int count, bool increasing)
    {
        if (count == 0) return 0;
        var last = increasing ? checked(start + count - 1) : checked(start - count + 1);
        return checked((long)count * (start + (long)last) / 2);
    }

    private static long EstimatePeakBytes(long pixels, long coordinates, bool insertion)
    {
        var rgba = checked(pixels * 4 * 3);
        var mask = pixels;
        var doubles = checked(pixels * 8 * 3);
        var predecessor = pixels;
        var mapping = insertion ? checked(pixels * 4) : 0;
        var paths = checked(coordinates * 4);
        var preview = checked(pixels * 4);
        var controlled = checked(rgba + mask + doubles + predecessor + mapping + paths + preview);
        return checked(controlled + ((controlled + 3) / 4));
    }
}

/// <summary>建立确定性双轴步骤顺序，并把输入、蒙版、请求和预算冻结进 fingerprint。</summary>
internal sealed class SeamResizePlanner(SeamResourceEstimator estimator)
{
    public SeamResizePlan Plan(string inputFingerprint, string maskFingerprint, ImageSize input,
        SeamResizeRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(maskFingerprint);
        var estimate = estimator.Estimate(input, request.TargetSize);
        if (!estimate.IsAllowed) throw new InvalidOperationException(string.Join(Environment.NewLine, estimate.BlockingReasons));
        var widthOperation = request.TargetSize.Width < input.Width ? SeamOperation.Remove : SeamOperation.Insert;
        var heightOperation = request.TargetSize.Height < input.Height ? SeamOperation.Remove : SeamOperation.Insert;
        var widthSteps = Math.Abs(request.TargetSize.Width - input.Width);
        var heightSteps = Math.Abs(request.TargetSize.Height - input.Height);
        var widthFirst = request.AxisOrder switch
        {
            SeamAxisOrder.WidthFirst => true,
            SeamAxisOrder.HeightFirst => false,
            _ => widthSteps / (double)input.Width >= heightSteps / (double)input.Height
        };
        var steps = new List<(SeamOrientation, SeamOperation)>(estimate.TotalSeams);
        void Append(SeamOrientation orientation, SeamOperation operation, int count)
        { for (var index = 0; index < count; index++) steps.Add((orientation, operation)); }
        if (widthFirst)
        { Append(SeamOrientation.Vertical, widthOperation, widthSteps); Append(SeamOrientation.Horizontal, heightOperation, heightSteps); }
        else
        { Append(SeamOrientation.Horizontal, heightOperation, heightSteps); Append(SeamOrientation.Vertical, widthOperation, widthSteps); }
        var identity = string.Create(CultureInfo.InvariantCulture,
            $"{SeamCarvingProtocols.Plan}|{inputFingerprint}|{maskFingerprint}|{input.Width}x{input.Height}|{request.TargetSize.Width}x{request.TargetSize.Height}|{request.AxisOrder}|{request.ReferenceAlgorithm}");
        return new(inputFingerprint, maskFingerprint, input, request, steps, estimate, SeamFingerprint.ForText(identity));
    }
}
