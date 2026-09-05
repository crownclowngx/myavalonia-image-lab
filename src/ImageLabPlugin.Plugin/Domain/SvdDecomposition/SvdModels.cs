using ImageLabPlugin.Domain.Shared.Analysis;
using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Domain.SvdDecomposition;

internal enum SvdColorStrategy
{
    SingleChannel,
    IndependentRgb,
    IndependentYCbCr
}

internal enum SvdEnergyStatus
{
    Available,
    NotApplicable
}

internal enum SvdComparisonCompletionStatus
{
    Complete,
    CancelledPartial
}

internal enum SvdFailureReason
{
    NotConverged,
    NumericValidationFailed
}

/// <summary>把未收敛与数值诊断失败保留为可区分的领域错误，而不是空结果或普通提示字符串。</summary>
internal sealed class SvdDecompositionException(SvdFailureReason reason, string message) : InvalidOperationException(message)
{
    public SvdFailureReason Reason { get; } = reason;
}

/// <summary>连续行优先、构造后只读的有限 double 矩阵。</summary>
/// <remarks>
/// 行固定对应图片 Y，列固定对应图片 X。构造函数复制调用方数据并拒绝非有限值，因此分解、缓存和
/// Rank-k 重建可以共享同一矩阵事实，不会被外部数组偷偷改写。本类型只保存尺寸与值，不承担任何算法。
/// </remarks>
internal sealed class DenseMatrix
{
    public const int MaximumElementCount = 65_536;
    public const double MaximumAbsoluteValue = 1e150;
    private readonly double[] _values;

    public DenseMatrix(int rows, int columns, ReadOnlySpan<double> values)
    {
        if (rows <= 0) throw new ArgumentOutOfRangeException(nameof(rows), rows, "矩阵行数必须为正数。");
        if (columns <= 0) throw new ArgumentOutOfRangeException(nameof(columns), columns, "矩阵列数必须为正数。");
        var count = checked(rows * columns);
        if (count > MaximumElementCount)
            throw new ArgumentOutOfRangeException(nameof(values), $"矩阵最多允许 {MaximumElementCount:N0} 个样本。");
        if (values.Length != count) throw new ArgumentException("矩阵样本数与行列尺寸不一致。", nameof(values));
        foreach (var value in values)
            if (!double.IsFinite(value) || Math.Abs(value) > MaximumAbsoluteValue)
                throw new ArgumentException("矩阵包含非有限值或超过数值协议上限的样本。", nameof(values));
        Rows = rows;
        Columns = columns;
        _values = values.ToArray();
    }

    private DenseMatrix(int rows, int columns, double[] ownedValues)
    {
        Rows = rows;
        Columns = columns;
        _values = ownedValues;
    }

    public int Rows { get; }
    public int Columns { get; }
    public int RankLimit => Math.Min(Rows, Columns);
    public ReadOnlyMemory<double> Values => _values;

    public double this[int row, int column]
    {
        get
        {
            if ((uint)row >= (uint)Rows || (uint)column >= (uint)Columns)
                throw new ArgumentOutOfRangeException(nameof(row), $"矩阵坐标 ({row},{column}) 越界。");
            return _values[(row * Columns) + column];
        }
    }

    public DenseMatrix Transpose()
    {
        var result = new double[_values.Length];
        for (var row = 0; row < Rows; row++)
            for (var column = 0; column < Columns; column++)
                result[(column * Rows) + row] = _values[(row * Columns) + column];
        return FromOwned(Columns, Rows, result);
    }

    internal static DenseMatrix FromOwned(int rows, int columns, double[] values) =>
        new(rows, columns, values);
}

internal sealed record SvdDiagnostics(
    bool Converged,
    int Sweeps,
    double MaximumUOrthogonalityError,
    double MaximumVOrthogonalityError,
    double RelativeReconstructionError,
    string NumericProtocol);

/// <summary>拥有经济型 U、奇异值与 V 的不可变分解结果。</summary>
/// <remarks>
/// U 按行优先保存为 rows×r，V 保存为 columns×r，r=min(rows,columns)。构造时复制缓冲并验证协议；
/// 此后只读暴露。向量整体正负号没有数学唯一性，所以消费者只能依赖重建和子空间，不应比较裸列身份。
/// </remarks>
internal sealed class SvdFactors
{
    private readonly double[] _u;
    private readonly double[] _singularValues;
    private readonly double[] _v;

    public SvdFactors(int rows, int columns, ReadOnlySpan<double> u, ReadOnlySpan<double> singularValues,
        ReadOnlySpan<double> v, SvdDiagnostics diagnostics)
    {
        if (rows <= 0 || columns <= 0) throw new ArgumentOutOfRangeException(nameof(rows), "分解尺寸必须为正数。");
        var rank = Math.Min(rows, columns);
        if (u.Length != checked(rows * rank) || singularValues.Length != rank || v.Length != checked(columns * rank))
            throw new ArgumentException("U、奇异值或 V 的尺寸不符合经济型 SVD 协议。");
        for (var index = 0; index < singularValues.Length; index++)
        {
            var value = singularValues[index];
            if (!double.IsFinite(value) || value < 0d)
                throw new ArgumentException("奇异值必须是有限非负数。", nameof(singularValues));
            if (index > 0 && value > singularValues[index - 1] * (1d + 1e-12))
                throw new ArgumentException("奇异值必须按降序保存。", nameof(singularValues));
        }
        if (!AllFinite(u) || !AllFinite(v)) throw new ArgumentException("奇异向量包含非有限值。");
        Rows = rows;
        Columns = columns;
        _u = u.ToArray();
        _singularValues = singularValues.ToArray();
        _v = v.ToArray();
        Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    public int Rows { get; }
    public int Columns { get; }
    public int RankLimit => _singularValues.Length;
    public ReadOnlyMemory<double> U => _u;
    public ReadOnlyMemory<double> SingularValues => _singularValues;
    public ReadOnlyMemory<double> V => _v;
    public SvdDiagnostics Diagnostics { get; }
    public double GetU(int row, int component) => _u[(row * RankLimit) + component];
    public double GetV(int column, int component) => _v[(column * RankLimit) + component];

    private static bool AllFinite(ReadOnlySpan<double> values)
    {
        foreach (var value in values) if (!double.IsFinite(value)) return false;
        return true;
    }
}

internal sealed record SingularValueEnergySample(
    int ComponentIndex,
    double SingularValue,
    double RelativeSingularValue,
    double EnergyShare,
    double CumulativeEnergy);

internal sealed record SingularValueEnergyReport(
    double TotalEnergy,
    IReadOnlyList<SingularValueEnergySample> Samples,
    int NumericRank,
    double NumericRankTolerance,
    SvdEnergyStatus Status);

internal sealed record SvdChannelFactors(ImageChannel Channel, double Neutral, DenseMatrix SourceMatrix, SvdFactors Factors);

internal sealed record SvdDecompositionSet(
    SvdColorStrategy Strategy,
    ImageChannel SingleChannel,
    string ProxyFingerprint,
    IReadOnlyList<SvdChannelFactors> Channels,
    TimeSpan Elapsed);

internal sealed record SvdMatrixError(
    ImageChannel Channel,
    double TheoreticalFrobeniusError,
    double DirectFrobeniusError,
    double RelativeFrobeniusError,
    double? RetainedEnergy,
    double RawMinimum,
    double RawMaximum);

internal sealed record SvdClippingDiagnostics(int ClippedPixels, int ClippedComponents);

internal sealed record SvdRankResult(
    SvdColorStrategy Strategy,
    ImageChannel SingleChannel,
    int Rank,
    string RecipeFingerprint,
    PixelImage Image,
    IReadOnlyList<DenseMatrix> ReconstructedMatrices,
    IReadOnlyList<SvdMatrixError> MatrixErrors,
    FullReferenceQualityMetrics Quality,
    SvdClippingDiagnostics Clipping,
    double? AggregateRetainedEnergy,
    TimeSpan Elapsed);

internal sealed record SvdComponentProjection(
    ImageChannel Channel,
    int ComponentIndex,
    double SingularValue,
    double? EnergyShare,
    double RawMinimum,
    double RawMaximum,
    double DisplayScale,
    PixelImage Preview);

internal sealed record SvdStrategyCase(
    SvdColorStrategy Strategy,
    int MatrixCount,
    int CommonRank,
    double? RetainedEnergy,
    FullReferenceQualityMetrics Quality,
    TimeSpan Elapsed);

internal sealed record SvdStrategyComparison(
    int CommonRank,
    IReadOnlyList<SvdStrategyCase> Cases,
    SvdComparisonCompletionStatus CompletionStatus);

internal sealed record SvdResourceEstimate(
    int Rows,
    int Columns,
    int ChannelCount,
    long MatrixDoubleCount,
    long EstimatedPeakBytes)
{
    public static SvdResourceEstimate Create(int rows, int columns, int channelCount)
    {
        if (rows <= 0 || columns <= 0) throw new ArgumentOutOfRangeException(nameof(rows));
        if (channelCount is not (1 or 3)) throw new ArgumentOutOfRangeException(nameof(channelCount));
        var samples = checked((long)rows * columns);
        if (samples > DenseMatrix.MaximumElementCount) throw new ArgumentOutOfRangeException(nameof(rows));
        var rank = Math.Min(rows, columns);
        // 每通道按输入、工作区、U、V、Rank 输出及最坏转置临时区估算；再加三张 RGBA 观察图。
        var doubles = checked(channelCount * ((4L * samples) + ((long)rows * rank) + ((long)columns * rank) + rank));
        var bytes = checked((doubles * sizeof(double)) + (3L * samples * 4L));
        return new(rows, columns, channelCount, doubles, bytes);
    }
}

internal static class SvdRecipeFingerprint
{
    public const string NumericProtocol = "one-sided-jacobi-v1";

    public static string Create(string proxyFingerprint, SvdColorStrategy strategy, ImageChannel channel, int rank) =>
        $"{NumericProtocol}|{proxyFingerprint}|{strategy}|{channel}|k={rank}";
}
