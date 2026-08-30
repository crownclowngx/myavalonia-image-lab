using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ImageLabPlugin.Domain.Watermarking;

namespace ImageLabPlugin.Domain.Robustness;

/// <summary>V1 显式支持的扰动种类。稳定英文 ID 用于快照和报告，枚举名称不直接写入持久数据。</summary>
internal enum PerturbationKind
{
    JpegReencode,
    Scale,
    GaussianNoise,
    SaltPepperNoise,
    DeterministicPixel,
    GaussianBlur,
    MedianBlur,
    UnsharpMask,
    Crop,
    Pad,
    Translate,
    Rotate,
    Perspective,
    Brightness,
    Contrast,
    Gamma,
    Saturation,
    ColorBias
}

internal static class PerturbationKindIds
{
    public static string ToStableId(this PerturbationKind kind) => kind switch
    {
        PerturbationKind.JpegReencode => "jpeg-reencode",
        PerturbationKind.Scale => "scale",
        PerturbationKind.GaussianNoise => "gaussian-noise",
        PerturbationKind.SaltPepperNoise => "salt-pepper-noise",
        PerturbationKind.DeterministicPixel => "deterministic-pixel",
        PerturbationKind.GaussianBlur => "gaussian-blur",
        PerturbationKind.MedianBlur => "median-blur",
        PerturbationKind.UnsharpMask => "unsharp-mask",
        PerturbationKind.Crop => "crop",
        PerturbationKind.Pad => "pad",
        PerturbationKind.Translate => "translate",
        PerturbationKind.Rotate => "rotate",
        PerturbationKind.Perspective => "perspective",
        PerturbationKind.Brightness => "brightness",
        PerturbationKind.Contrast => "contrast",
        PerturbationKind.Gamma => "gamma",
        PerturbationKind.Saturation => "saturation",
        PerturbationKind.ColorBias => "color-bias",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知扰动种类。")
    };

    public static PerturbationKind Parse(string value) => Enum.GetValues<PerturbationKind>()
        .FirstOrDefault(kind => kind.ToStableId() == value, (PerturbationKind)(-1)) is var result && Enum.IsDefined(result)
            ? result
            : throw new ArgumentException($"不支持的扰动 ID：{value}", nameof(value));
}

/// <summary>扰动参数的强类型基类；算法不接收字符串字典，也不在热循环中解析文本。</summary>
internal abstract record PerturbationParameters;
internal sealed record JpegParameters(int Quality = 95) : PerturbationParameters;
internal sealed record ScaleParameters(decimal ScaleX = 1m, decimal ScaleY = 1m) : PerturbationParameters;
internal sealed record GaussianNoiseParameters(decimal Sigma = 0m) : PerturbationParameters;
internal sealed record SaltPepperParameters(decimal Ratio = 0m) : PerturbationParameters;
internal sealed record DeterministicPixelParameters(int Amplitude = 0) : PerturbationParameters;
internal sealed record GaussianBlurParameters(decimal Sigma = 0m) : PerturbationParameters;
internal sealed record MedianBlurParameters(int KernelSize = 3) : PerturbationParameters;
internal sealed record UnsharpMaskParameters(decimal Amount = 0m) : PerturbationParameters;
internal sealed record CropParameters(int Left = 0, int Top = 0, int Right = 0, int Bottom = 0) : PerturbationParameters;
internal readonly record struct RgbaColor(byte R, byte G, byte B, byte A = 255);
internal sealed record PadParameters(int Left = 0, int Top = 0, int Right = 0, int Bottom = 0, RgbaColor Fill = default) : PerturbationParameters;
internal sealed record TranslateParameters(int Dx = 0, int Dy = 0, RgbaColor Fill = default) : PerturbationParameters;
internal sealed record RotateParameters(decimal Degrees = 0m, RgbaColor Fill = default) : PerturbationParameters;
internal sealed record PerspectiveParameters(decimal TopLeftX = 0m, decimal TopLeftY = 0m, decimal TopRightX = 0m, decimal TopRightY = 0m, decimal BottomRightX = 0m, decimal BottomRightY = 0m, decimal BottomLeftX = 0m, decimal BottomLeftY = 0m, RgbaColor Fill = default) : PerturbationParameters;
internal sealed record BrightnessParameters(int Offset = 0) : PerturbationParameters;
internal sealed record ContrastParameters(decimal Factor = 1m) : PerturbationParameters;
internal sealed record GammaParameters(decimal Gamma = 1m) : PerturbationParameters;
internal sealed record SaturationParameters(decimal Factor = 1m) : PerturbationParameters;
internal sealed record ColorBiasParameters(int Red = 0, int Green = 0, int Blue = 0) : PerturbationParameters;

internal sealed record PerturbationStep(string StepId, PerturbationKind Kind, bool Enabled, PerturbationParameters Parameters)
{
    public static PerturbationStep Create(PerturbationKind kind, PerturbationParameters parameters) =>
        new(Guid.NewGuid().ToString("N"), kind, true, parameters);
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
    public RobustnessValidationResult Validate(RobustnessRecipe recipe, IReadOnlyList<EmbeddingProfileId> profiles)
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

/// <summary>扫描值替换和参数边界的唯一事实源，避免计划器、UI 与算子各写一套范围。</summary>
internal static class PerturbationParameterEditor
{
    public static PerturbationStep WithScannedValue(PerturbationStep step, string parameterId, decimal value)
    {
        PerturbationParameters parameters = (step.Parameters, parameterId) switch
        {
            (JpegParameters, "quality") => new JpegParameters(ToInt(value)),
            (ScaleParameters p, "scale-x") => p with { ScaleX = value },
            (ScaleParameters p, "scale-y") => p with { ScaleY = value },
            (GaussianNoiseParameters, "sigma") => new GaussianNoiseParameters(value),
            (SaltPepperParameters, "ratio") => new SaltPepperParameters(value),
            (DeterministicPixelParameters, "amplitude") => new DeterministicPixelParameters(ToInt(value)),
            (GaussianBlurParameters, "sigma") => new GaussianBlurParameters(value),
            (MedianBlurParameters, "kernel-size") => new MedianBlurParameters(ToInt(value)),
            (UnsharpMaskParameters, "amount") => new UnsharpMaskParameters(value),
            (BrightnessParameters, "offset") => new BrightnessParameters(ToInt(value)),
            (ContrastParameters, "factor") => new ContrastParameters(value),
            (GammaParameters, "gamma") => new GammaParameters(value),
            (SaturationParameters, "factor") => new SaturationParameters(value),
            (RotateParameters p, "degrees") => p with { Degrees = value },
            (TranslateParameters p, "dx") => p with { Dx = ToInt(value) },
            (TranslateParameters p, "dy") => p with { Dy = ToInt(value) },
            (CropParameters p, "left") => p with { Left = ToInt(value) },
            (CropParameters p, "top") => p with { Top = ToInt(value) },
            (CropParameters p, "right") => p with { Right = ToInt(value) },
            (CropParameters p, "bottom") => p with { Bottom = ToInt(value) },
            (PadParameters p, "left") => p with { Left = ToInt(value) },
            (PadParameters p, "top") => p with { Top = ToInt(value) },
            (PadParameters p, "right") => p with { Right = ToInt(value) },
            (PadParameters p, "bottom") => p with { Bottom = ToInt(value) },
            (PerspectiveParameters p, "top-left-x") => p with { TopLeftX = value },
            (PerspectiveParameters p, "top-left-y") => p with { TopLeftY = value },
            (PerspectiveParameters p, "top-right-x") => p with { TopRightX = value },
            (PerspectiveParameters p, "top-right-y") => p with { TopRightY = value },
            (PerspectiveParameters p, "bottom-right-x") => p with { BottomRightX = value },
            (PerspectiveParameters p, "bottom-right-y") => p with { BottomRightY = value },
            (PerspectiveParameters p, "bottom-left-x") => p with { BottomLeftX = value },
            (PerspectiveParameters p, "bottom-left-y") => p with { BottomLeftY = value },
            (ColorBiasParameters p, "red") => p with { Red = ToInt(value) },
            (ColorBiasParameters p, "green") => p with { Green = ToInt(value) },
            (ColorBiasParameters p, "blue") => p with { Blue = ToInt(value) },
            _ => throw new ArgumentException($"参数 {parameterId} 不能作为 {step.Kind.ToStableId()} 的扫描轴。")
        };
        var updated = step with { Parameters = parameters };
        Validate(updated);
        return updated;
    }

    public static void Validate(PerturbationStep step)
    {
        var valid = step.Parameters switch
        {
            JpegParameters p => p.Quality is >= 1 and <= 100,
            ScaleParameters p => InRange(p.ScaleX, 0.05m, 8m) && InRange(p.ScaleY, 0.05m, 8m),
            GaussianNoiseParameters p => InRange(p.Sigma, 0m, 100m),
            SaltPepperParameters p => InRange(p.Ratio, 0m, 1m),
            DeterministicPixelParameters p => p.Amplitude is >= 0 and <= 255,
            GaussianBlurParameters p => InRange(p.Sigma, 0m, 10m),
            MedianBlurParameters p => p.KernelSize is 3 or 5,
            UnsharpMaskParameters p => InRange(p.Amount, 0m, 5m),
            CropParameters p => NonNegative(p.Left, p.Top, p.Right, p.Bottom),
            PadParameters p => NonNegative(p.Left, p.Top, p.Right, p.Bottom),
            TranslateParameters p => Math.Abs((long)p.Dx) <= 100_000 && Math.Abs((long)p.Dy) <= 100_000,
            RotateParameters p => InRange(p.Degrees, -15m, 15m),
            PerspectiveParameters p => new[] { p.TopLeftX, p.TopLeftY, p.TopRightX, p.TopRightY, p.BottomRightX, p.BottomRightY, p.BottomLeftX, p.BottomLeftY }.All(value => InRange(value, -0.1m, 0.1m)),
            BrightnessParameters p => p.Offset is >= -255 and <= 255,
            ContrastParameters p => InRange(p.Factor, 0m, 4m),
            GammaParameters p => InRange(p.Gamma, 0.1m, 10m),
            SaturationParameters p => InRange(p.Factor, 0m, 4m),
            ColorBiasParameters p => p.Red is >= -255 and <= 255 && p.Green is >= -255 and <= 255 && p.Blue is >= -255 and <= 255,
            _ => false
        };
        if (!valid || !Matches(step.Kind, step.Parameters)) throw new ArgumentException("参数类型、数值范围或算子种类不匹配。");
    }

    private static bool Matches(PerturbationKind kind, PerturbationParameters value) => (kind, value) switch
    {
        (PerturbationKind.JpegReencode, JpegParameters) or (PerturbationKind.Scale, ScaleParameters) or
        (PerturbationKind.GaussianNoise, GaussianNoiseParameters) or (PerturbationKind.SaltPepperNoise, SaltPepperParameters) or
        (PerturbationKind.DeterministicPixel, DeterministicPixelParameters) or (PerturbationKind.GaussianBlur, GaussianBlurParameters) or
        (PerturbationKind.MedianBlur, MedianBlurParameters) or (PerturbationKind.UnsharpMask, UnsharpMaskParameters) or
        (PerturbationKind.Crop, CropParameters) or (PerturbationKind.Pad, PadParameters) or
        (PerturbationKind.Translate, TranslateParameters) or (PerturbationKind.Rotate, RotateParameters) or
        (PerturbationKind.Perspective, PerspectiveParameters) or (PerturbationKind.Brightness, BrightnessParameters) or
        (PerturbationKind.Contrast, ContrastParameters) or (PerturbationKind.Gamma, GammaParameters) or
        (PerturbationKind.Saturation, SaturationParameters) or (PerturbationKind.ColorBias, ColorBiasParameters) => true,
        _ => false
    };

    private static bool InRange(decimal value, decimal minimum, decimal maximum) => value >= minimum && value <= maximum;
    private static bool NonNegative(params int[] values) => values.All(value => value >= 0);
    private static int ToInt(decimal value) => decimal.Truncate(value) == value
        ? decimal.ToInt32(value)
        : throw new ArgumentException("该扫描参数只接受整数值。");
}

internal readonly record struct RobustnessCaseKey(EmbeddingProfileId Profile, int ScanPointIndex, decimal CanonicalValue, int TrialIndex)
{
    public override string ToString() => $"{Profile}:{ScanPointIndex}:{CanonicalValue.ToString(CultureInfo.InvariantCulture)}:{TrialIndex}";
}

internal sealed record RobustnessPlannedCase(RobustnessCaseKey Key, IReadOnlyList<PerturbationStep> Steps);
internal sealed record RobustnessExecutionPlan(RobustnessRecipe Recipe, IReadOnlyList<RobustnessPlannedCase> Cases, string RecipeHash);

internal sealed class RobustnessExperimentPlanner(RobustnessRecipeValidator validator)
{
    public RobustnessExecutionPlan Plan(RobustnessRecipe recipe, IReadOnlyList<EmbeddingProfileId> profiles)
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

    private static string ComputeHash(RobustnessRecipe recipe, IReadOnlyList<EmbeddingProfileId> profiles, IReadOnlyList<decimal> points)
    {
        var canonical = new StringBuilder($"v{recipe.SchemaVersion}|{recipe.ExperimentSeed}|{recipe.TrialCount}|{recipe.ProbeEachStep}");
        foreach (var profile in profiles.OrderBy(value => value)) canonical.Append('|').Append((byte)profile);
        foreach (var step in recipe.Steps) canonical.Append('|').Append(step.StepId).Append(':').Append(step.Kind.ToStableId()).Append(':').Append(step.Enabled).Append(':').Append(step.Parameters);
        foreach (var point in points) canonical.Append('|').Append(point.ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))[..16].ToLowerInvariant();
    }
}
