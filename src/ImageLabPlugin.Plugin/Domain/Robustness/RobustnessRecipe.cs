using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ImageLabPlugin.Domain.Shared.Perturbations;

namespace ImageLabPlugin.Domain.Robustness;

internal enum RobustnessProfileId : byte
{
    Stealth = 1,
    Balanced = 2,
    Robust = 3
}

internal abstract record RobustnessScan
{
    public abstract IReadOnlyList<decimal> Expand();
}

internal sealed record ExplicitValueScan(IReadOnlyList<decimal> Values) : RobustnessScan
{
    public override IReadOnlyList<decimal> Expand()
    {
        var result = new List<decimal>();
        var seen = new HashSet<decimal>();
        foreach (var value in Values)
        {
            if (seen.Add(value)) result.Add(value);
        }
        return result;
    }
}

internal sealed record DecimalRangeScan(decimal Start, decimal End, decimal Step) : RobustnessScan
{
    /// <summary>使用 decimal 逐点展开，避免 double 累计误差意外多生成一个端点。</summary>
    public override IReadOnlyList<decimal> Expand()
    {
        if (Step <= 0m || End < Start) throw new ArgumentException("扫描范围要求终点不小于起点且步长大于零。");
        var result = new List<decimal>();
        for (var value = Start; value <= End; value = checked(value + Step))
        {
            result.Add(value);
            if (result.Count > RobustnessLimits.MaximumScanPoints) break;
        }
        return result;
    }
}

internal sealed record RobustnessScanAxis(string StepId, string ParameterId, RobustnessScan Values);

internal sealed record RobustnessRecipe(
    int SchemaVersion,
    IReadOnlyList<PerturbationStep> Steps,
    RobustnessScanAxis Scan,
    int TrialCount,
    ulong ExperimentSeed,
    bool ProbeEachStep = true)
{
    public const int CurrentSchemaVersion = 1;
}

internal static class RobustnessLimits
{
    public const int MaximumSteps = 12;
    public const int MaximumScanPoints = 101;
    public const int MaximumTrials = 20;
    public const int MaximumCases = 300;
    public const int MaximumObservations = 1_200;
}

internal sealed record RobustnessValidationResult(IReadOnlyList<string> Errors, int ScanPointCount, int CaseCount, int ObservationCount)
{
    public bool IsValid => Errors.Count == 0;
    public void ThrowIfInvalid()
    {
        if (!IsValid) throw new ArgumentException(string.Join("；", Errors));
    }
}

/// <summary>只验证领域配方和资源乘积；不执行算子，也不读取文件。</summary>
internal sealed class RobustnessRecipeValidator
{
    public RobustnessValidationResult Validate(RobustnessRecipe recipe, IReadOnlyList<RobustnessProfileId> profiles)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        var errors = new List<string>();
        if (recipe.SchemaVersion != RobustnessRecipe.CurrentSchemaVersion) errors.Add($"不支持配方 schema {recipe.SchemaVersion}");
        if (recipe.Steps.Count is 0 or > RobustnessLimits.MaximumSteps) errors.Add($"扰动步骤数必须为 1–{RobustnessLimits.MaximumSteps}");
        if (recipe.TrialCount is < 1 or > RobustnessLimits.MaximumTrials) errors.Add($"重复次数必须为 1–{RobustnessLimits.MaximumTrials}");
        if (profiles.Count is 0 or > 3 || profiles.Distinct().Count() != profiles.Count) errors.Add("Profile 必须选择 1–3 个且不能重复");
        if (recipe.Steps.Select(step => step.StepId).Any(string.IsNullOrWhiteSpace) || recipe.Steps.Select(step => step.StepId).Distinct(StringComparer.Ordinal).Count() != recipe.Steps.Count)
            errors.Add("StepId 必须非空且在配方内唯一");

        IReadOnlyList<decimal> points = [];
        try { points = recipe.Scan.Values.Expand(); }
        catch (Exception exception) when (exception is ArgumentException or OverflowException) { errors.Add(exception.Message); }
        if (points.Count is 0 or > RobustnessLimits.MaximumScanPoints) errors.Add($"扫描点数必须为 1–{RobustnessLimits.MaximumScanPoints}");
        var matchingTargets = recipe.Steps.Where(step => step.StepId == recipe.Scan.StepId).Take(2).ToArray();
        var target = matchingTargets.Length == 1 ? matchingTargets[0] : null;
        if (target is null || !target.Enabled) errors.Add("扫描目标步骤不存在或已禁用");
        else foreach (var point in points)
        {
            try { _ = PerturbationParameterEditor.WithScannedValue(target, recipe.Scan.ParameterId, point); }
            catch (ArgumentException exception) { errors.Add(exception.Message); break; }
        }
        foreach (var step in recipe.Steps)
        {
            try { PerturbationParameterEditor.Validate(step); }
            catch (ArgumentException exception) { errors.Add($"步骤 {step.StepId}：{exception.Message}"); }
        }

        var enabled = recipe.Steps.Count(step => step.Enabled);
        var cases = 0;
        var observations = 0;
        try
        {
            cases = checked(points.Count * recipe.TrialCount * profiles.Count);
            observations = recipe.ProbeEachStep ? checked(cases * enabled) : cases;
            if (cases > RobustnessLimits.MaximumCases) errors.Add($"完整案例数 {cases} 超过上限 {RobustnessLimits.MaximumCases}");
            if (observations > RobustnessLimits.MaximumObservations) errors.Add($"观察数 {observations} 超过上限 {RobustnessLimits.MaximumObservations}");
        }
        catch (OverflowException) { errors.Add("资源乘积发生整数溢出"); }
        return new(errors.Distinct(StringComparer.Ordinal).ToArray(), points.Count, cases, observations);
    }
}

internal readonly record struct RobustnessCaseKey(RobustnessProfileId Profile, int ScanPointIndex, decimal CanonicalValue, int TrialIndex)
{
    public override string ToString() => $"{Profile}:{ScanPointIndex}:{CanonicalValue.ToString(CultureInfo.InvariantCulture)}:{TrialIndex}";
}

internal sealed record RobustnessPlannedCase(RobustnessCaseKey Key, IReadOnlyList<PerturbationStep> Steps);
internal sealed record RobustnessExecutionPlan(RobustnessRecipe Recipe, IReadOnlyList<RobustnessPlannedCase> Cases, string RecipeHash);

internal sealed class RobustnessExperimentPlanner(RobustnessRecipeValidator validator)
{
    public RobustnessExecutionPlan Plan(RobustnessRecipe recipe, IReadOnlyList<RobustnessProfileId> profiles)
    {
        validator.Validate(recipe, profiles).ThrowIfInvalid();
        var points = recipe.Scan.Values.Expand();
        var cases = new List<RobustnessPlannedCase>();
        foreach (var profile in profiles.OrderBy(value => value))
            foreach (var (point, pointIndex) in points.Select((value, index) => (value, index)))
                foreach (var trial in Enumerable.Range(0, recipe.TrialCount))
                {
                    var steps = recipe.Steps.Select(step => step.StepId == recipe.Scan.StepId
                        ? PerturbationParameterEditor.WithScannedValue(step, recipe.Scan.ParameterId, point)
                        : step).ToArray();
                    cases.Add(new(new(profile, pointIndex, point, trial), steps));
                }
        return new(recipe, cases, ComputeHash(recipe, profiles, points));
    }

    private static string ComputeHash(RobustnessRecipe recipe, IReadOnlyList<RobustnessProfileId> profiles, IReadOnlyList<decimal> points)
    {
        var canonical = new StringBuilder($"v{recipe.SchemaVersion}|{recipe.ExperimentSeed}|{recipe.TrialCount}|{recipe.ProbeEachStep}");
        foreach (var profile in profiles.OrderBy(value => value)) canonical.Append('|').Append((byte)profile);
        foreach (var step in recipe.Steps) canonical.Append('|').Append(step.StepId).Append(':').Append(step.Kind.ToStableId()).Append(':').Append(step.Enabled).Append(':').Append(step.Parameters);
        foreach (var point in points) canonical.Append('|').Append(point.ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))[..16].ToLowerInvariant();
    }
}
