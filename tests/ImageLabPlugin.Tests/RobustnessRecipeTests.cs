using ImageLabPlugin.Domain.Robustness;
using ImageLabPlugin.Domain.Shared.Perturbations;
using ImageLabPlugin.Domain.Watermarking;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class RobustnessRecipeTests
{
    [Fact]
    public void Decimal范围不会因浮点累计生成额外端点() =>
        Assert.Equal([0m, 0.1m, 0.2m, 0.3m], new DecimalRangeScan(0m, 0.3m, 0.1m).Expand());

    [Fact]
    public void 显式列表按首次出现顺序去重() => Assert.Equal([3m, 1m, 2m], new ExplicitValueScan([3m, 1m, 3m, 2m]).Expand());

    [Fact]
    public void 相同配方计划案例键顺序和哈希稳定()
    {
        var recipe = Recipe(new ExplicitValueScan([5m, 10m]), trials: 2); var planner = new RobustnessExperimentPlanner(new());
        var first = planner.Plan(recipe, [RobustnessProfileId.Robust, RobustnessProfileId.Stealth]);
        var second = planner.Plan(recipe, [RobustnessProfileId.Stealth, RobustnessProfileId.Robust]);
        Assert.Equal(first.RecipeHash, second.RecipeHash); Assert.Equal(first.Cases.Select(x => x.Key), second.Cases.Select(x => x.Key));
        Assert.Equal(RobustnessProfileId.Stealth, first.Cases[0].Key.Profile); Assert.Equal(8, first.Cases.Count);
    }

    [Fact]
    public void 重复StepId与超限案例在执行前失败()
    {
        var step = new PerturbationStep("same", PerturbationKind.GaussianNoise, true, new GaussianNoiseParameters());
        var recipe = new RobustnessRecipe(1, [step, step], new("same", "sigma", new ExplicitValueScan(Enumerable.Range(0, 101).Select(x => (decimal)x).ToArray())), 20, 1);
        var result = new RobustnessRecipeValidator().Validate(recipe, [RobustnessProfileId.Stealth, RobustnessProfileId.Balanced, RobustnessProfileId.Robust]);
        Assert.False(result.IsValid); Assert.Contains(result.Errors, x => x.Contains("唯一", StringComparison.Ordinal)); Assert.Contains(result.Errors, x => x.Contains("案例数", StringComparison.Ordinal));
    }

    [Fact]
    public void 未知扫描参数失效安全失败()
    {
        var recipe = Recipe(new ExplicitValueScan([1m])) with { Scan = new("noise", "unknown", new ExplicitValueScan([1m])) };
        Assert.Throws<ArgumentException>(() => new RobustnessExperimentPlanner(new()).Plan(recipe, [RobustnessProfileId.Balanced]));
    }

    internal static RobustnessRecipe Recipe(RobustnessScan scan, int trials = 1) => new(1,
        [new PerturbationStep("noise", PerturbationKind.GaussianNoise, true, new GaussianNoiseParameters())], new("noise", "sigma", scan), trials, 42);
}
