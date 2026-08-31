namespace ImageLabPlugin.Domain.SvdDecomposition;

/// <summary>对一个有界稠密矩阵执行确定性的双精度单边 Jacobi SVD。</summary>
/// <remarks>
/// 这里直接正交化 A 的列，而不先构造 AᵀA；后者会把条件数平方，使小奇异值更早落入舍入误差。
/// 算法只负责一个矩阵，不知道颜色策略、缓存、文件或 UI。宽矩阵只转置一次再交换 U/V，最终暴露的
/// 尺寸始终对应原矩阵。列对按固定字典序扫描，保证同一运行时上的结果和诊断可复现。
/// </remarks>
internal sealed class JacobiSvdDecomposer
{
    internal const int MaximumSweeps = 64;
    internal const double RelativeOrthogonalityTolerance = 1e-12;
    internal const double ValidationTolerance = 2e-9;
    internal const double MachineEpsilon = 2.2204460492503131e-16;
    private readonly int _maximumSweeps;
    private readonly double _relativeOrthogonalityTolerance;

    public JacobiSvdDecomposer() : this(MaximumSweeps, RelativeOrthogonalityTolerance)
    {
    }

    /// <summary>只供确定性门禁缩短 sweep 上限；生产组合根始终使用冻结的公开构造函数。</summary>
    internal JacobiSvdDecomposer(int maximumSweeps, double relativeOrthogonalityTolerance)
    {
        if (maximumSweeps < 0) throw new ArgumentOutOfRangeException(nameof(maximumSweeps));
        if (!double.IsFinite(relativeOrthogonalityTolerance) || relativeOrthogonalityTolerance <= 0d)
            throw new ArgumentOutOfRangeException(nameof(relativeOrthogonalityTolerance));
        _maximumSweeps = maximumSweeps;
        _relativeOrthogonalityTolerance = relativeOrthogonalityTolerance;
    }

    public SvdFactors Decompose(DenseMatrix matrix, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        if (matrix.Rows >= matrix.Columns)
        return DecomposeTall(matrix, cancellationToken);

        // Aᵀ=ŨΣṼᵀ => A=ṼΣŨᵀ。因此原矩阵 U=Ṽ、V=Ũ；交换的只是所有权，
        // 奇异值顺序、符号对和诊断仍来自同一次分解，不让调用方感知内部转置。
        var transposed = matrix.Transpose();
        var factors = DecomposeTall(transposed, cancellationToken);
        var diagnostics = Validate(
            matrix,
            factors.V.Span,
            factors.SingularValues.Span,
            factors.U.Span,
            factors.Diagnostics.Sweeps,
            factors.Diagnostics.Converged);
        return new SvdFactors(matrix.Rows, matrix.Columns, factors.V.Span,
            factors.SingularValues.Span, factors.U.Span, diagnostics);
    }

    private SvdFactors DecomposeTall(DenseMatrix matrix, CancellationToken cancellationToken)
    {
        var rows = matrix.Rows;
        var columns = matrix.Columns;
        var work = matrix.Values.ToArray();
        var right = new double[checked(columns * columns)];
        for (var index = 0; index < columns; index++) right[(index * columns) + index] = 1d;

        var converged = columns <= 1;
        var completedSweeps = 0;
        for (var sweep = 0; sweep < _maximumSweeps && !converged; sweep++)
        {
            var rotations = 0;
            for (var p = 0; p < columns - 1; p++)
            for (var q = p + 1; q < columns; q++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (alpha, beta, gamma) = ScaledColumnProducts(work, rows, columns, p, q);
                if (alpha == 0d || beta == 0d ||
                    Math.Abs(gamma) <= _relativeOrthogonalityTolerance * Math.Sqrt(alpha * beta))
                    continue;

                // 稳定形式避免直接计算 tan(2θ)。当 ζ 很大时，分母的 abs(ζ)+hypot(1,ζ)
                // 不会发生灾难性相消；γ=0 已在上方跳过。
                var zeta = (beta - alpha) / (2d * gamma);
                var tangent = Math.CopySign(1d, zeta) /
                    (Math.Abs(zeta) + Math.Sqrt(1d + (zeta * zeta)));
                var cosine = 1d / Math.Sqrt(1d + (tangent * tangent));
                var sine = cosine * tangent;
                RotateColumns(work, rows, columns, p, q, cosine, sine, cancellationToken);
                RotateColumns(right, columns, columns, p, q, cosine, sine, cancellationToken);
                rotations++;
            }

            completedSweeps = sweep + 1;
            converged = rotations == 0;
        }

        if (!converged)
            throw new SvdDecompositionException(SvdFailureReason.NotConverged,
                $"单边 Jacobi SVD 在 {_maximumSweeps} 个 sweep 后仍未收敛。");

        var singularValues = new double[columns];
        var left = new double[checked(rows * columns)];
        for (var column = 0; column < columns; column++)
        {
            var norm = ColumnNorm(work, rows, columns, column);
            singularValues[column] = norm;
            if (norm == 0d) continue;
            for (var row = 0; row < rows; row++)
                left[(row * columns) + column] = work[(row * columns) + column] / norm;
        }

        StableSortDescending(singularValues, left, right, rows, columns);
        NormalizeSigns(left, right, singularValues, rows, columns);
        var diagnostics = Validate(matrix, left, singularValues, right, completedSweeps, converged);
        if (diagnostics.MaximumUOrthogonalityError > ValidationTolerance ||
            diagnostics.MaximumVOrthogonalityError > ValidationTolerance ||
            diagnostics.RelativeReconstructionError > ValidationTolerance)
        {
            throw new SvdDecompositionException(SvdFailureReason.NumericValidationFailed,
                $"SVD 数值诊断未通过：U={diagnostics.MaximumUOrthogonalityError:E3}，" +
                $"V={diagnostics.MaximumVOrthogonalityError:E3}，重建={diagnostics.RelativeReconstructionError:E3}。");
        }
        return new SvdFactors(rows, columns, left, singularValues, right, diagnostics);
    }

    /// <summary>
    /// 先按列对的共同最大绝对值缩放，再计算 α、β、γ。三个量使用同一尺度，Jacobi 角度不变，
    /// 同时避免极大有限输入在平方时溢出。Kahan 补偿降低长列点积中小项被吞掉的风险。
    /// </summary>
    private static (double Alpha, double Beta, double Gamma) ScaledColumnProducts(
        double[] values, int rows, int columns, int p, int q)
    {
        var scale = 0d;
        for (var row = 0; row < rows; row++)
        {
            scale = Math.Max(scale, Math.Abs(values[(row * columns) + p]));
            scale = Math.Max(scale, Math.Abs(values[(row * columns) + q]));
        }
        if (scale == 0d) return (0d, 0d, 0d);
        double alpha = 0d, beta = 0d, gamma = 0d;
        double alphaCompensation = 0d, betaCompensation = 0d, gammaCompensation = 0d;
        for (var row = 0; row < rows; row++)
        {
            var pValue = values[(row * columns) + p] / scale;
            var qValue = values[(row * columns) + q] / scale;
            AddKahan(pValue * pValue, ref alpha, ref alphaCompensation);
            AddKahan(qValue * qValue, ref beta, ref betaCompensation);
            AddKahan(pValue * qValue, ref gamma, ref gammaCompensation);
        }
        return (alpha, beta, gamma);
    }

    private static void RotateColumns(double[] values, int rows, int columns, int p, int q,
        double cosine, double sine, CancellationToken cancellationToken)
    {
        for (var row = 0; row < rows; row++)
        {
            if ((row & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
            var pIndex = (row * columns) + p;
            var qIndex = (row * columns) + q;
            var pValue = values[pIndex];
            var qValue = values[qIndex];
            values[pIndex] = (cosine * pValue) - (sine * qValue);
            values[qIndex] = (sine * pValue) + (cosine * qValue);
        }
    }

    private static double ColumnNorm(double[] values, int rows, int columns, int column)
    {
        var scale = 0d;
        for (var row = 0; row < rows; row++) scale = Math.Max(scale, Math.Abs(values[(row * columns) + column]));
        if (scale == 0d) return 0d;
        double sum = 0d, compensation = 0d;
        for (var row = 0; row < rows; row++)
        {
            var normalized = values[(row * columns) + column] / scale;
            AddKahan(normalized * normalized, ref sum, ref compensation);
        }
        var norm = scale * Math.Sqrt(sum);
        if (!double.IsFinite(norm)) throw new InvalidOperationException("SVD 列范数超出有限数值范围。");
        return norm;
    }

    private static void StableSortDescending(double[] sigma, double[] left, double[] right, int rows, int columns)
    {
        var order = Enumerable.Range(0, columns).OrderByDescending(index => sigma[index]).ThenBy(index => index).ToArray();
        var sortedSigma = new double[columns];
        var sortedLeft = new double[left.Length];
        var sortedRight = new double[right.Length];
        for (var target = 0; target < columns; target++)
        {
            var source = order[target];
            sortedSigma[target] = sigma[source];
            for (var row = 0; row < rows; row++) sortedLeft[(row * columns) + target] = left[(row * columns) + source];
            for (var row = 0; row < columns; row++) sortedRight[(row * columns) + target] = right[(row * columns) + source];
        }
        sortedSigma.CopyTo(sigma, 0);
        sortedLeft.CopyTo(left, 0);
        sortedRight.CopyTo(right, 0);
    }

    private static void NormalizeSigns(double[] left, double[] right, double[] sigma, int rows, int rank)
    {
        for (var component = 0; component < rank; component++)
        {
            if (sigma[component] == 0d) continue;
            var pivotRow = 0;
            var pivotAbsolute = 0d;
            for (var row = 0; row < rows; row++)
            {
                var absolute = Math.Abs(left[(row * rank) + component]);
                if (absolute > pivotAbsolute) { pivotAbsolute = absolute; pivotRow = row; }
            }
            if (left[(pivotRow * rank) + component] >= 0d) continue;
            for (var row = 0; row < rows; row++) left[(row * rank) + component] = -left[(row * rank) + component];
            for (var row = 0; row < rank; row++) right[(row * rank) + component] = -right[(row * rank) + component];
        }
    }

    private static SvdDiagnostics Validate(DenseMatrix source, ReadOnlySpan<double> left,
        ReadOnlySpan<double> sigma, ReadOnlySpan<double> right, int sweeps, bool converged)
    {
        var rank = sigma.Length;
        var sigmaFloor = sigma.Length == 0 ? 0d : sigma[0] * Math.Max(source.Rows, source.Columns) * MachineEpsilon * 32d;
        var maximumU = OrthogonalityError(left, source.Rows, rank, sigma, sigmaFloor, skipNumericalZero: true);
        var maximumV = OrthogonalityError(right, source.Columns, rank, sigma, sigmaFloor, skipNumericalZero: false);
        double residualScale = 0d, residualSum = 1d, sourceScale = 0d, sourceSum = 1d;
        var original = source.Values.Span;
        for (var row = 0; row < source.Rows; row++)
        for (var column = 0; column < source.Columns; column++)
        {
            double reconstructed = 0d;
            for (var component = 0; component < rank; component++)
                reconstructed += sigma[component] * left[(row * rank) + component] * right[(column * rank) + component];
            AddScaledSquare(original[(row * source.Columns) + column] - reconstructed, ref residualScale, ref residualSum);
            AddScaledSquare(original[(row * source.Columns) + column], ref sourceScale, ref sourceSum);
        }
        var residual = residualScale == 0d ? 0d : residualScale * Math.Sqrt(residualSum);
        var sourceNorm = sourceScale == 0d ? 0d : sourceScale * Math.Sqrt(sourceSum);
        var relative = sourceNorm == 0d ? residual : residual / sourceNorm;
        return new(converged, sweeps, maximumU, maximumV, relative, SvdRecipeFingerprint.NumericProtocol);
    }

    private static double OrthogonalityError(ReadOnlySpan<double> vectors, int rows, int columns,
        ReadOnlySpan<double> sigma, double sigmaFloor, bool skipNumericalZero)
    {
        var maximum = 0d;
        for (var p = 0; p < columns; p++)
        {
            if (skipNumericalZero && sigma[p] <= sigmaFloor) continue;
            for (var q = p; q < columns; q++)
            {
                if (skipNumericalZero && sigma[q] <= sigmaFloor) continue;
                double dot = 0d, compensation = 0d;
                for (var row = 0; row < rows; row++)
                    AddKahan(vectors[(row * columns) + p] * vectors[(row * columns) + q], ref dot, ref compensation);
                maximum = Math.Max(maximum, Math.Abs(dot - (p == q ? 1d : 0d)));
            }
        }
        return maximum;
    }

    internal static void AddKahan(double value, ref double sum, ref double compensation)
    {
        var adjusted = value - compensation;
        var next = sum + adjusted;
        compensation = (next - sum) - adjusted;
        sum = next;
    }

    internal static void AddScaledSquare(double value, ref double scale, ref double sum)
    {
        var absolute = Math.Abs(value);
        if (absolute == 0d) return;
        if (scale < absolute)
        {
            var ratio = scale / absolute;
            sum = 1d + (sum * ratio * ratio);
            scale = absolute;
        }
        else
        {
            var ratio = absolute / scale;
            sum += ratio * ratio;
        }
    }
}
