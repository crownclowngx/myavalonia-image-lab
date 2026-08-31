using ImageLabPlugin.Domain.Imaging;
using ImageLabPlugin.Domain.PoissonBlending;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class PoissonProblemAndSolverTests
{
    [Theory]
    [InlineData(0, 3)]
    [InlineData(1, 3)]
    [InlineData(2, 1)]
    public void 问题构造按模式冻结通道数且不建立N平方矩阵(int modeValue, int channels)
    {
        var mode = (PoissonBlendMode)modeValue;
        var source = PoissonTestFactory.Gradient(6, 6); var target = PoissonTestFactory.Solid(8, 8, 80, 90, 100);
        var mask = PoissonTestFactory.RectangleMask(6, 6, new(2, 2, 2, 2));
        var problem = PoissonTestFactory.Builder().Build(source, target, mask, new(1, 1), new(mode), default);
        Assert.Equal(4, problem.UnknownCount); Assert.Equal(channels, problem.ChannelCount);
        Assert.Equal(16, problem.NeighborIndices.Length); Assert.Equal(4 * channels, problem.Rhs.Length);
    }

    [Fact]
    public void 常量源和目标的初值残差为零并在第零轮收敛()
    {
        var image = PoissonTestFactory.Solid(5, 5, 128, 128, 128); var mask = PoissonTestFactory.RectangleMask(5, 5, new(2, 2, 1, 1));
        var options = new PoissonBlendOptions(PoissonBlendMode.NormalClone);
        var problem = PoissonTestFactory.Builder().Build(image, image, mask, default, options);
        var state = new PoissonRelaxationSolver().CreateState(problem, options);
        Assert.Equal(PoissonStopReason.Converged, state.StopReason); Assert.Equal(0, state.Iteration); Assert.Equal(0d, state.History[0].Rms, 14);
    }

    [Fact]
    public void 单未知首轮严格执行rhs除四()
    {
        var problem = ManualProblem(PoissonBlendMode.Monochrome, [4d], [0d], [-1, -1, -1, -1], [1], [1]);
        var options = new PoissonBlendOptions(PoissonBlendMode.Monochrome, 1e-8, 1e-7, 5, 1);
        var solver = new PoissonRelaxationSolver(); var state = solver.CreateState(problem, options); solver.Step(problem, state, options);
        Assert.Equal(1d, state.Values[0], 14); Assert.Equal(PoissonStopReason.Converged, state.StopReason);
    }

    [Fact]
    public void 两未知收敛到独立手算解()
    {
        var neighbors = new[] { -1, 1, -1, -1, 0, -1, -1, -1 };
        var problem = ManualProblem(PoissonBlendMode.Monochrome, [3d, 6d], [0d, 0d], neighbors, [1, 2], [1, 1]);
        var options = new PoissonBlendOptions(PoissonBlendMode.Monochrome, 1e-8, 1e-7, 200, 1);
        var solver = new PoissonRelaxationSolver(); var state = solver.CreateState(problem, options); solver.Run(problem, state, options);
        Assert.Equal(1.2d, state.Values[0], 7); Assert.Equal(1.8d, state.Values[1], 7); Assert.Equal(PoissonStopReason.Converged, state.StopReason);
    }

    [Fact]
    public void 单步N次与连续运行N次逐double一致()
    {
        var problem = ManualProblem(PoissonBlendMode.Monochrome, [3d, 6d], [0d, 0d], [-1, 1, -1, -1, 0, -1, -1, -1], [1, 2], [1, 1]);
        var options = new PoissonBlendOptions(PoissonBlendMode.Monochrome, 1e-8, 1e-7, 4, 1); var solver = new PoissonRelaxationSolver();
        var stepped = solver.CreateState(problem, options); while (stepped.StopReason is null) solver.Step(problem, stepped, options);
        var run = solver.CreateState(problem, options); solver.Run(problem, run, options);
        Assert.Equal(stepped.Values, run.Values); Assert.Equal(stepped.History, run.History); Assert.Equal(stepped.StopReason, run.StopReason);
    }

    [Fact]
    public void 取消不会提交半个sweep()
    {
        var problem = ManualProblem(PoissonBlendMode.Monochrome, [4d], [0d], [-1, -1, -1, -1], [1], [1]);
        var options = new PoissonBlendOptions(PoissonBlendMode.Monochrome, 1e-8, 1e-7, 5, 1); var solver = new PoissonRelaxationSolver(); var state = solver.CreateState(problem, options);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => solver.Step(problem, state, options, cancellation.Token));
        Assert.Equal(0d, state.Values[0]); Assert.Equal(0, state.Iteration); Assert.Equal(PoissonStopReason.Canceled, state.StopReason);
    }

    [Fact]
    public void 双阈值必须同时满足()
    {
        var problem = ManualProblem(PoissonBlendMode.Monochrome, [4d], [0d], [-1, -1, -1, -1], [1], [1]);
        var looseRmsStrictMax = new PoissonBlendOptions(PoissonBlendMode.Monochrome, 1e-3, 1e-7, 1, 1);
        var state = new PoissonRelaxationSolver().CreateState(problem, looseRmsStrictMax);
        Assert.Null(state.StopReason);
    }

    private static PoissonProblem ManualProblem(PoissonBlendMode mode, double[] rhs, double[] initial, int[] neighbors, int[] x, int[] y)
    {
        var count = x.Length; var topology = new PoissonMaskTopology(count, new(1, 1, count, 1), 1, 0, count);
        var resource = new PoissonResourceEstimate(count, count, mode == PoissonBlendMode.Monochrome ? 1 : 3, 100, 1000, []);
        return new("manual", mode, new ImageSize(5, 5), x, y, x, y, neighbors, rhs, initial, topology, resource, 0, 0);
    }
}
