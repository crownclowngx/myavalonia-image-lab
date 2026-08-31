using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Domain.PoissonBlending;

internal enum PoissonBlendMode { NormalClone, MixedGradient, Monochrome }
internal enum PoissonMaskTool { Add, Erase }
internal enum PoissonStopReason { Converged, IterationLimit, Canceled, BudgetExceeded, NonFinite, Stale, Faulted }
internal enum PoissonSessionState { Empty, ImagesReady, MaskReady, PlacementReady, ProblemReady, Paused, Running, Converged, Canceled, Faulted, Stale, Disposed }

internal static class PoissonProtocols
{
    public const string Numeric = "poisson-linear-srgb-dirichlet-rbgs-v1";
    public const string Mask = "poisson-binary-mask-even-rounding-v1";
    public const string Budget = "poisson-resource-budget-v1";
    public const string ReportSchema = "image-lab-poisson-blending-report/v1";
    public const string SnapshotSchema = "image-lab-poisson-blending-document/v1";
}

/// <summary>源坐标到目标坐标的整数平移；目标坐标始终为 <c>(sx + Dx, sy + Dy)</c>。</summary>
internal readonly record struct ImageOffset(int Dx, int Dy);

/// <summary>整数像素闭开矩形 <c>[Left,Right) × [Top,Bottom)</c>。</summary>
internal readonly record struct PoissonRectangle(int Left, int Top, int Width, int Height)
{
    public int Right => checked(Left + Width);
    public int Bottom => checked(Top + Height);
    public bool IsEmpty => Width == 0 || Height == 0;

    public PoissonRectangle Validate(ImageSize size)
    {
        if (Left < 0 || Top < 0 || Width < 0 || Height < 0 || Right > size.Width || Bottom > size.Height)
            throw new ArgumentOutOfRangeException(nameof(size), "闭开矩形必须完整位于源图内，宽和高允许为零。 ");
        return this;
    }
}

internal readonly record struct PoissonNormalizedPoint(double X, double Y)
{
    public void Validate()
    {
        if (!double.IsFinite(X) || !double.IsFinite(Y) || X is < 0d or > 1d || Y is < 0d or > 1d)
            throw new ArgumentOutOfRangeException(nameof(X), "归一化坐标必须是 [0,1] 内的有限数。 ");
    }
}

/// <summary>
/// 可持久化的二值画笔意图。归一化坐标不包含 DPI、缩放或滚动状态；后写笔划覆盖先写笔划。
/// </summary>
internal sealed record PoissonMaskStroke(
    PoissonMaskTool Tool,
    double RadiusNormalized,
    IReadOnlyList<PoissonNormalizedPoint> Points,
    int Sequence)
{
    public const int MaximumPoints = 2_048;

    public PoissonMaskStroke Validate()
    {
        if (!Enum.IsDefined(Tool)) throw new ArgumentOutOfRangeException(nameof(Tool));
        if (!double.IsFinite(RadiusNormalized) || RadiusNormalized is <= 0d or > 0.25d)
            throw new ArgumentOutOfRangeException(nameof(RadiusNormalized), "归一化半径必须位于 (0,0.25]。 ");
        if (Points.Count is 0 or > MaximumPoints)
            throw new ArgumentOutOfRangeException(nameof(Points), $"每条笔划必须包含 1 至 {MaximumPoints} 个点。 ");
        if (Sequence < 0) throw new ArgumentOutOfRangeException(nameof(Sequence));
        foreach (var point in Points) point.Validate();
        return this;
    }
}

internal sealed record PoissonMaskDefinition(
    PoissonRectangle? Rectangle,
    IReadOnlyList<PoissonMaskStroke> Strokes,
    string RasterProtocol = PoissonProtocols.Mask);

/// <summary>与源图同尺寸的二值求解域；数组由对象独占，对外只读。</summary>
internal sealed class PoissonBinaryMask
{
    private readonly byte[] _values;

    public PoissonBinaryMask(ImageSize size, ReadOnlySpan<byte> values)
    {
        if (values.Length != size.PixelCount) throw new ArgumentException("遮罩长度必须等于源图像素数。", nameof(values));
        foreach (var value in values) if (value is not 0 and not 1) throw new ArgumentException("二值遮罩只能包含 0 或 1。", nameof(values));
        Size = size;
        _values = values.ToArray();
    }

    internal PoissonBinaryMask(ImageSize size) : this(size, new byte[checked((int)size.PixelCount)]) { }
    public ImageSize Size { get; }
    public ReadOnlyMemory<byte> Values => _values;
    public bool Contains(int x, int y) => _values[GetIndex(x, y)] == 1;
    internal void Set(int x, int y, bool included) => _values[GetIndex(x, y)] = included ? (byte)1 : (byte)0;

    private int GetIndex(int x, int y)
    {
        if ((uint)x >= (uint)Size.Width || (uint)y >= (uint)Size.Height)
            throw new ArgumentOutOfRangeException(nameof(x), $"遮罩坐标 ({x},{y}) 超出 {Size.Width}×{Size.Height}。 ");
        return checked((y * Size.Width) + x);
    }
}

internal sealed record PoissonMaskTopology(
    int UnknownCount,
    PoissonRectangle BoundingBox,
    int ComponentCount,
    int HoleCount,
    int BoundaryCount);

internal sealed record PoissonBlendOptions(
    PoissonBlendMode Mode,
    double RmsTolerance = 1e-6,
    double MaxAbsTolerance = 1e-5,
    int MaxIterations = 800,
    int PreviewInterval = 10)
{
    public PoissonBlendOptions Validate()
    {
        if (!Enum.IsDefined(Mode)) throw new ArgumentOutOfRangeException(nameof(Mode));
        if (!double.IsFinite(RmsTolerance) || RmsTolerance is < 1e-8 or > 1e-3)
            throw new ArgumentOutOfRangeException(nameof(RmsTolerance), "RMS 容差必须位于 [1e-8,1e-3]。 ");
        if (!double.IsFinite(MaxAbsTolerance) || MaxAbsTolerance is < 1e-7 or > 1e-2)
            throw new ArgumentOutOfRangeException(nameof(MaxAbsTolerance), "最大残差容差必须位于 [1e-7,1e-2]。 ");
        if (MaxIterations is < 1 or > 2_000) throw new ArgumentOutOfRangeException(nameof(MaxIterations));
        if (PreviewInterval is not (1 or 5 or 10 or 25 or 50)) throw new ArgumentOutOfRangeException(nameof(PreviewInterval));
        return this;
    }
}

internal sealed record PoissonPlacementIssue(string Code, string Message, int? SourceX = null, int? SourceY = null,
    int? TargetX = null, int? TargetY = null, byte? ActualAlpha = null);

internal sealed record PoissonPlacementValidation(IReadOnlyList<PoissonPlacementIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}

internal sealed record PoissonResourceEstimate(
    int UnknownCount,
    long BoundingBoxPixels,
    int ChannelCount,
    long ScalarUpdates,
    long EstimatedPeakBytes,
    IReadOnlyList<string> BlockingReasons)
{
    public bool IsAllowed => BlockingReasons.Count == 0;
}

/// <summary>
/// 紧凑 Poisson 方程。Unknown 坐标按目标 y/x 排序；NeighborIndices 每个未知固定存左、右、上、下，
/// -1 表示该邻居是使用目标颜色的 Dirichlet 边界。大数组不参与 record 结构比较，也不进入快照。
/// </summary>
internal sealed class PoissonProblem
{
    public PoissonProblem(string fingerprint, PoissonBlendMode mode, ImageSize targetSize,
        ReadOnlySpan<int> sourceX, ReadOnlySpan<int> sourceY, ReadOnlySpan<int> targetX, ReadOnlySpan<int> targetY,
        ReadOnlySpan<int> neighborIndices, ReadOnlySpan<double> rhs, ReadOnlySpan<double> initialValues,
        PoissonMaskTopology topology, PoissonResourceEstimate resourceEstimate, long sourceGuidanceEdges,
        long targetGuidanceEdges)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        var count = sourceX.Length;
        if (sourceY.Length != count || targetX.Length != count || targetY.Length != count || neighborIndices.Length != count * 4)
            throw new ArgumentException("未知量坐标或四邻接数组长度不一致。 ");
        var channels = mode == PoissonBlendMode.Monochrome ? 1 : 3;
        if (rhs.Length != count * channels || initialValues.Length != rhs.Length)
            throw new ArgumentException("RHS/初值长度必须等于 unknown×channelCount。 ");
        if (rhs.ContainsNonFinite() || initialValues.ContainsNonFinite()) throw new ArgumentException("RHS 和初值必须都是有限数。 ");
        Fingerprint = fingerprint; Mode = mode; TargetSize = targetSize; ChannelCount = channels;
        SourceX = sourceX.ToArray(); SourceY = sourceY.ToArray(); TargetX = targetX.ToArray(); TargetY = targetY.ToArray();
        NeighborIndices = neighborIndices.ToArray(); Rhs = rhs.ToArray(); InitialValues = initialValues.ToArray();
        Topology = topology; ResourceEstimate = resourceEstimate; SourceGuidanceEdges = sourceGuidanceEdges;
        TargetGuidanceEdges = targetGuidanceEdges;
    }

    public string Fingerprint { get; }
    public PoissonBlendMode Mode { get; }
    public ImageSize TargetSize { get; }
    public int ChannelCount { get; }
    public int UnknownCount => SourceX.Length;
    public int[] SourceX { get; }
    public int[] SourceY { get; }
    public int[] TargetX { get; }
    public int[] TargetY { get; }
    public int[] NeighborIndices { get; }
    public double[] Rhs { get; }
    public double[] InitialValues { get; }
    public PoissonMaskTopology Topology { get; }
    public PoissonResourceEstimate ResourceEstimate { get; }
    public long SourceGuidanceEdges { get; }
    public long TargetGuidanceEdges { get; }
}

internal sealed record PoissonResidual(int Iteration, double Rms, double MaxAbs, double RelativeRms);

/// <summary>一个 Document 独占的可变解；只在完整 sweep 结束后增加 Iteration。</summary>
internal sealed class PoissonSolverState
{
    private readonly List<PoissonResidual> _history = [];
    internal PoissonSolverState(string fingerprint, double[] values, PoissonResidual initial)
    { Fingerprint = fingerprint; Values = values; InitialRms = initial.Rms; _history.Add(initial); }
    public string Fingerprint { get; }
    public double[] Values { get; }
    public int Iteration { get; internal set; }
    public double InitialRms { get; }
    public PoissonStopReason? StopReason { get; internal set; }
    public IReadOnlyList<PoissonResidual> History => _history;
    internal void Add(PoissonResidual residual) => _history.Add(residual);
}

internal sealed record PoissonClampStatistics(long ClippedChannelCount, long ClippedPixelCount);
internal sealed record PoissonComposedImage(PixelImage Image, PoissonClampStatistics ClampStatistics);

internal sealed record PoissonBlendDiagnostics(
    double BoundaryGuidanceRmse,
    double InteriorGradientRmse,
    double ResidualRms,
    double ResidualMaxAbs,
    double? MixedSourceEdgeRatio,
    PoissonClampStatistics ClampStatistics);

internal sealed record PoissonBlendResult(
    string ProblemFingerprint,
    PixelImage Output,
    PixelImage AlphaBaseline,
    PoissonBlendDiagnostics Diagnostics,
    PoissonResidual Convergence,
    PoissonStopReason StopReason);

internal static class PoissonFingerprint
{
    public static string ForImage(PixelImage image)
    {
        Span<byte> dimensions = stackalloc byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(dimensions, image.Size.Width);
        BinaryPrimitives.WriteInt32LittleEndian(dimensions[4..], image.Size.Height);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(dimensions); hash.AppendData(image.Rgba.Span);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public static string ForProblem(PixelImage source, PixelImage target, PoissonBinaryMask mask,
        ImageOffset offset, PoissonBlendMode mode)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(ForImage(source)));
        hash.AppendData(Encoding.UTF8.GetBytes(ForImage(target)));
        hash.AppendData(mask.Values.Span);
        Span<byte> facts = stackalloc byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(facts, offset.Dx);
        BinaryPrimitives.WriteInt32LittleEndian(facts[4..], offset.Dy);
        BinaryPrimitives.WriteInt32LittleEndian(facts[8..], (int)mode);
        hash.AppendData(facts); hash.AppendData(Encoding.UTF8.GetBytes(PoissonProtocols.Numeric));
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}

file static class PoissonSpanValidation
{
    public static bool ContainsNonFinite(this ReadOnlySpan<double> values)
    { foreach (var value in values) if (!double.IsFinite(value)) return true; return false; }
}
