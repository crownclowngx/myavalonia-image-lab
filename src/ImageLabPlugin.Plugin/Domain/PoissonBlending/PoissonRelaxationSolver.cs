namespace ImageLabPlugin.Domain.PoissonBlending;

/// <summary>
/// 确定性红黑 Gauss–Seidel 求解器。棋盘色由目标坐标 <c>(tx+ty)&amp;1</c> 决定；一次 Step 严格包含红、黑、
/// 全域残差三个阶段。取消时恢复 sweep 前的值，因此调用方永远看不到半轮状态；UI 提交节拍不进入数值核心。
/// </summary>
internal sealed class PoissonRelaxationSolver
{
    public PoissonSolverState CreateState(PoissonProblem problem, PoissonBlendOptions options)
    {
        ArgumentNullException.ThrowIfNull(problem); options.Validate(); EnsureCompatible(problem, options);
        var values = (double[])problem.InitialValues.Clone();
        var initial = Measure(problem, values, 0, 0d);
        var state = new PoissonSolverState(problem.Fingerprint, values, initial);
        if (initial.Rms <= options.RmsTolerance && initial.MaxAbs <= options.MaxAbsTolerance)
            state.StopReason = PoissonStopReason.Converged;
        return state;
    }

    public PoissonResidual Step(PoissonProblem problem, PoissonSolverState state, PoissonBlendOptions options,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(problem); ArgumentNullException.ThrowIfNull(state); options.Validate();
        EnsureCompatible(problem, options);
        if (!string.Equals(problem.Fingerprint, state.Fingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException("解状态与问题 fingerprint 不匹配，旧结果已经过期。 ");
        if (state.StopReason is not null) return state.History[^1];
        if (state.Iteration >= options.MaxIterations) { state.StopReason = PoissonStopReason.IterationLimit; return state.History[^1]; }

        var backup = (double[])state.Values.Clone();
        try
        {
            UpdateColor(problem, state.Values, 0, token);
            UpdateColor(problem, state.Values, 1, token);
            token.ThrowIfCancellationRequested();
            var iteration = state.Iteration + 1;
            var residual = Measure(problem, state.Values, iteration, state.InitialRms);
            if (!double.IsFinite(residual.Rms) || !double.IsFinite(residual.MaxAbs))
            { Array.Copy(backup, state.Values, backup.Length); state.StopReason = PoissonStopReason.NonFinite; return state.History[^1]; }
            state.Iteration = iteration; state.Add(residual);
            state.StopReason = residual.Rms <= options.RmsTolerance && residual.MaxAbs <= options.MaxAbsTolerance
                ? PoissonStopReason.Converged
                : iteration >= options.MaxIterations ? PoissonStopReason.IterationLimit : null;
            return residual;
        }
        catch (OperationCanceledException)
        {
            Array.Copy(backup, state.Values, backup.Length);
            state.StopReason = PoissonStopReason.Canceled;
            throw;
        }
    }

    public PoissonSolverState Run(PoissonProblem problem, PoissonSolverState state, PoissonBlendOptions options,
        Func<bool>? pauseRequested = null, Action<PoissonResidual>? completedSweep = null,
        CancellationToken token = default)
    {
        while (state.StopReason is null && !(pauseRequested?.Invoke() ?? false))
        {
            var residual = Step(problem, state, options, token);
            completedSweep?.Invoke(residual);
        }
        return state;
    }

    internal static PoissonResidual Measure(PoissonProblem problem, ReadOnlySpan<double> values, int iteration,
        double initialRms)
    {
        var channels = problem.ChannelCount; double sumSquares = 0d; double maxAbs = 0d;
        for (var i = 0; i < problem.UnknownCount; i++) for (var channel = 0; channel < channels; channel++)
        {
            var flat = (i * channels) + channel; var lhs = 4d * values[flat];
            for (var direction = 0; direction < 4; direction++)
            {
                var neighbor = problem.NeighborIndices[(i * 4) + direction];
                if (neighbor >= 0) lhs -= values[(neighbor * channels) + channel];
            }
            var residual = problem.Rhs[flat] - lhs;
            sumSquares += residual * residual; maxAbs = Math.Max(maxAbs, Math.Abs(residual));
        }
        var rms = Math.Sqrt(sumSquares / checked(problem.UnknownCount * channels));
        var relative = rms / Math.Max(iteration == 0 ? rms : initialRms, 1e-15d);
        return new(iteration, rms, maxAbs, relative);
    }

    private static void UpdateColor(PoissonProblem problem, Span<double> values, int color, CancellationToken token)
    {
        var channels = problem.ChannelCount;
        for (var i = 0; i < problem.UnknownCount; i++)
        {
            if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
            if (((problem.TargetX[i] + problem.TargetY[i]) & 1) != color) continue;
            for (var channel = 0; channel < channels; channel++)
            {
                var sum = problem.Rhs[(i * channels) + channel];
                for (var direction = 0; direction < 4; direction++)
                {
                    var neighbor = problem.NeighborIndices[(i * 4) + direction];
                    if (neighbor >= 0) sum += values[(neighbor * channels) + channel];
                }
                var value = sum / 4d;
                if (!double.IsFinite(value)) throw new ArithmeticException("Poisson 解出现非有限数。 ");
                values[(i * channels) + channel] = value;
            }
        }
    }

    private static void EnsureCompatible(PoissonProblem problem, PoissonBlendOptions options)
    {
        if (problem.Mode != options.Mode) throw new InvalidOperationException("问题模式与求解选项不一致。 ");
    }
}
