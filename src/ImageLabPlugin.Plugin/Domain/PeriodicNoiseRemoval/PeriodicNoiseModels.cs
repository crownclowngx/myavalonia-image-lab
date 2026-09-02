using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ImageLabPlugin.Domain.Shared.Spectral;
using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Domain.PeriodicNoiseRemoval;

internal enum PeriodicNotchTransition { Ideal, Butterworth, Gaussian }
internal enum PeriodicNotchOrigin { Manual, Automatic }
internal enum PeriodicPeakRiskLevel { Low, Medium, High }

[Flags]
internal enum PeriodicPeakRiskReason
{
    None = 0,
    NearDc = 1,
    NearNyquist = 2,
    BroadPeakOrRidge = 4,
    DenseNeighborhood = 8,
    LowProminence = 16,
    SelfConjugate = 32,
    LargeSuggestedLoss = 64
}

/// <summary>以 cycles/pixel 保存一个与代理尺寸无关的中心化频率。</summary>
/// <remarks>
/// 两个分量都位于 <c>[-0.5, 0.5)</c>。配方不保存画布像素或某次 FFT 的裸 bin，因此同一中心可以按固定舍入规则
/// 映射到代理和预算内原尺寸频谱。共轭频率通过环面上的取负得到，Nyquist 的 <c>0.5</c> 会规范回 <c>-0.5</c>。
/// </remarks>
internal readonly record struct PeriodicFrequency
{
    public PeriodicFrequency(double fx, double fy)
    {
        if (!double.IsFinite(fx) || fx < -0.5d || fx >= 0.5d)
            throw new ArgumentOutOfRangeException(nameof(fx), "fx 必须有限且位于 [-0.5,0.5)。");
        if (!double.IsFinite(fy) || fy < -0.5d || fy >= 0.5d)
            throw new ArgumentOutOfRangeException(nameof(fy), "fy 必须有限且位于 [-0.5,0.5)。");
        Fx = fx == 0d ? 0d : fx;
        Fy = fy == 0d ? 0d : fy;
    }

    public double Fx { get; }
    public double Fy { get; }
    public double Radius => Math.Sqrt((Fx * Fx) + (Fy * Fy));
    public double PeriodPixels => Radius <= 0d ? double.PositiveInfinity : 1d / Radius;
    public double DirectionDegrees => Math.Atan2(Fy, Fx) * 180d / Math.PI;

    public PeriodicFrequency Conjugate() => new(Wrap(-Fx), Wrap(-Fy));

    public static PeriodicFrequency FromInternal(int x, int y, int width, int height)
    {
        var point = FrequencyCoordinates.FromInternal(x, y, width, height);
        return new PeriodicFrequency(point.Fx, point.Fy);
    }

    public (int X, int Y) ToInternal(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        var kx = (int)Math.Round(Fx * width, MidpointRounding.AwayFromZero);
        var ky = (int)Math.Round(Fy * height, MidpointRounding.AwayFromZero);
        return ((kx % width + width) % width, (ky % height + height) % height);
    }

    public static PeriodicFrequency Canonical(PeriodicFrequency value)
    {
        var conjugate = value.Conjugate();
        return Compare(value, conjugate) <= 0 ? value : conjugate;
    }

    public static double ToroidalDistance(PeriodicFrequency first, PeriodicFrequency second)
    {
        var dx = Math.Abs(first.Fx - second.Fx);
        var dy = Math.Abs(first.Fy - second.Fy);
        dx = Math.Min(dx, 1d - dx);
        dy = Math.Min(dy, 1d - dy);
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static int Compare(PeriodicFrequency first, PeriodicFrequency second)
    {
        var x = first.Fx.CompareTo(second.Fx);
        return x != 0 ? x : first.Fy.CompareTo(second.Fy);
    }

    private static double Wrap(double value)
    {
        while (value < -0.5d) value += 1d;
        while (value >= 0.5d) value -= 1d;
        return value == 0d ? 0d : value;
    }
}

/// <summary>冻结候选检测阈值和结构上限的不可变设置。</summary>
/// <remarks>
/// 默认值来自合成正弦 Golden：排除 DC 周围 0.025 cycles/pixel，稳健分数至少 6，局部突出度至少 0.2。
/// 候选最多 64 对，自动建议最多 12 对；构造门禁防止 UI 或导入数据绕过资源预算。
/// </remarks>
internal sealed record PeriodicNoiseDetectionSettings
{
    public PeriodicNoiseDetectionSettings(double dcExclusionRadius = 0.025d, double robustScoreThreshold = 6d,
        double prominenceThreshold = 0.2d, double suppressionRadius = 0.0125d, int maximumCandidates = 64,
        int maximumSuggestions = 12)
    {
        ValidatePositive(dcExclusionRadius, 0.25d, nameof(dcExclusionRadius));
        ValidatePositive(robustScoreThreshold, 100d, nameof(robustScoreThreshold));
        ValidatePositive(prominenceThreshold, 100d, nameof(prominenceThreshold));
        ValidatePositive(suppressionRadius, 0.25d, nameof(suppressionRadius));
        if (maximumCandidates is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(maximumCandidates));
        if (maximumSuggestions is < 0 or > 12 || maximumSuggestions > maximumCandidates)
            throw new ArgumentOutOfRangeException(nameof(maximumSuggestions));
        DcExclusionRadius = dcExclusionRadius;
        RobustScoreThreshold = robustScoreThreshold;
        ProminenceThreshold = prominenceThreshold;
        SuppressionRadius = suppressionRadius;
        MaximumCandidates = maximumCandidates;
        MaximumSuggestions = maximumSuggestions;
    }

    public double DcExclusionRadius { get; }
    public double RobustScoreThreshold { get; }
    public double ProminenceThreshold { get; }
    public double SuppressionRadius { get; }
    public int MaximumCandidates { get; }
    public int MaximumSuggestions { get; }

    private static void ValidatePositive(double value, double maximum, string parameter)
    {
        if (!double.IsFinite(value) || value <= 0d || value > maximum)
            throw new ArgumentOutOfRangeException(parameter);
    }
}

/// <summary>描述一对确定性频率峰及其可复核风险事实，不宣称它一定是噪声。</summary>
internal sealed record PeriodicFrequencyCandidate
{
    public PeriodicFrequencyCandidate(PeriodicFrequency canonicalFrequency, PeriodicFrequency conjugateFrequency,
        double robustScore, double prominence, double localCompactness, PeriodicPeakRiskLevel riskLevel,
        PeriodicPeakRiskReason riskReasons, int canonicalLinearIndex)
    {
        if (!double.IsFinite(robustScore) || !double.IsFinite(prominence) || !double.IsFinite(localCompactness))
            throw new ArgumentOutOfRangeException(nameof(robustScore), "候选数值必须有限。");
        if (localCompactness is < 0d or > 1d) throw new ArgumentOutOfRangeException(nameof(localCompactness));
        if (!Enum.IsDefined(riskLevel) || (riskReasons & ~AllRisks) != 0)
            throw new ArgumentOutOfRangeException(nameof(riskLevel));
        if (canonicalLinearIndex < 0) throw new ArgumentOutOfRangeException(nameof(canonicalLinearIndex));
        CanonicalFrequency = PeriodicFrequency.Canonical(canonicalFrequency);
        ConjugateFrequency = CanonicalFrequency.Conjugate();
        if (PeriodicFrequency.ToroidalDistance(ConjugateFrequency, conjugateFrequency) > 1e-12)
            throw new ArgumentException("候选共轭频率与 canonical 中心不一致。", nameof(conjugateFrequency));
        RobustScore = robustScore;
        Prominence = prominence;
        LocalCompactness = localCompactness;
        RiskLevel = riskLevel;
        RiskReasons = riskReasons;
        CanonicalLinearIndex = canonicalLinearIndex;
    }

    private const PeriodicPeakRiskReason AllRisks = PeriodicPeakRiskReason.NearDc |
        PeriodicPeakRiskReason.NearNyquist | PeriodicPeakRiskReason.BroadPeakOrRidge |
        PeriodicPeakRiskReason.DenseNeighborhood | PeriodicPeakRiskReason.LowProminence |
        PeriodicPeakRiskReason.SelfConjugate | PeriodicPeakRiskReason.LargeSuggestedLoss;

    public PeriodicFrequency CanonicalFrequency { get; }
    public PeriodicFrequency ConjugateFrequency { get; }
    public double RobustScore { get; }
    public double Prominence { get; }
    public double LocalCompactness { get; }
    public PeriodicPeakRiskLevel RiskLevel { get; }
    public PeriodicPeakRiskReason RiskReasons { get; }
    public int CanonicalLinearIndex { get; }
    public bool IsSafeSuggestion => RiskLevel != PeriodicPeakRiskLevel.High && RiskReasons == PeriodicPeakRiskReason.None;
}

/// <summary>保存一个 canonical 陷波中心、来源和启用状态。</summary>
internal sealed record PeriodicNotch
{
    public PeriodicNotch(PeriodicFrequency canonicalFrequency, PeriodicNotchOrigin origin, bool enabled = true)
    {
        if (!Enum.IsDefined(origin)) throw new ArgumentOutOfRangeException(nameof(origin));
        CanonicalFrequency = PeriodicFrequency.Canonical(canonicalFrequency);
        Origin = origin;
        Enabled = enabled;
    }

    public PeriodicFrequency CanonicalFrequency { get; }
    public PeriodicNotchOrigin Origin { get; }
    public bool Enabled { get; }
}

/// <summary>周期陷波数学与解释字段的不可变配方。</summary>
/// <remarks>
/// 所有启用中心共享半径、强度、过渡和阶数。构造时复制、规范化、排序并去重最多 32 对中心，使同一语义具有稳定指纹；
/// 完整指纹包含来源，数学指纹排除来源但包含启用状态，因而手动/自动来源不会污染相同遮罩的数值缓存。
/// </remarks>
internal sealed class PeriodicNoiseRecipe
{
    public const int CurrentSchemaVersion = 1;
    public const string ProductId = "myavalonia.plugin.image.lab.document.periodic-noise-removal";
    private readonly PeriodicNotch[] _notches;

    public PeriodicNoiseRecipe(ImageChannel channel, PeriodicNotchTransition transition, double radius,
        double strength, int butterworthOrder, IEnumerable<PeriodicNotch> notches, int schemaVersion = CurrentSchemaVersion)
    {
        if (!Enum.IsDefined(channel)) throw new ArgumentOutOfRangeException(nameof(channel));
        if (!Enum.IsDefined(transition)) throw new ArgumentOutOfRangeException(nameof(transition));
        if (!double.IsFinite(radius) || radius <= 0d || radius > 0.25d)
            throw new ArgumentOutOfRangeException(nameof(radius), "陷波半径必须位于 (0,0.25] cycles/pixel。");
        if (!double.IsFinite(strength) || strength is < 0d or > 1d)
            throw new ArgumentOutOfRangeException(nameof(strength), "衰减强度必须位于 [0,1]。");
        if (transition == PeriodicNotchTransition.Butterworth && butterworthOrder is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(butterworthOrder));
        if (schemaVersion != CurrentSchemaVersion) throw new ArgumentOutOfRangeException(nameof(schemaVersion));
        ArgumentNullException.ThrowIfNull(notches);
        var normalized = notches.Select(item => new PeriodicNotch(item.CanonicalFrequency, item.Origin, item.Enabled))
            .GroupBy(item => item.CanonicalFrequency)
            .Select(group => group.First())
            .OrderBy(item => item.CanonicalFrequency.Fx)
            .ThenBy(item => item.CanonicalFrequency.Fy)
            .ToArray();
        if (normalized.Length > 32) throw new ArgumentException("陷波中心最多 32 对。", nameof(notches));
        Channel = channel;
        Transition = transition;
        Radius = radius;
        Strength = strength;
        ButterworthOrder = transition == PeriodicNotchTransition.Butterworth ? butterworthOrder : 1;
        SchemaVersion = schemaVersion;
        _notches = normalized;
    }

    public ImageChannel Channel { get; }
    public PeriodicNotchTransition Transition { get; }
    public double Radius { get; }
    public double Strength { get; }
    public int ButterworthOrder { get; }
    public int SchemaVersion { get; }
    public IReadOnlyList<PeriodicNotch> Notches => Array.AsReadOnly(_notches);
    public int EnabledNotchCount => _notches.Count(item => item.Enabled);

    public PeriodicNoiseRecipe WithNotches(IEnumerable<PeriodicNotch> notches) =>
        new(Channel, Transition, Radius, Strength, ButterworthOrder, notches, SchemaVersion);

    public string Fingerprint() => Hash(includeOrigin: true);
    public string MathematicalFingerprint() => Hash(includeOrigin: false);

    private string Hash(bool includeOrigin)
    {
        var builder = new StringBuilder();
        builder.Append(includeOrigin ? "periodic-noise-recipe-v1" : "periodic-noise-math-v1");
        builder.Append('|').Append((int)Channel).Append('|').Append((int)Transition).Append('|')
            .Append(Radius.ToString("R", CultureInfo.InvariantCulture)).Append('|')
            .Append(Strength.ToString("R", CultureInfo.InvariantCulture)).Append('|').Append(ButterworthOrder);
        foreach (var notch in _notches)
        {
            builder.Append('|').Append(notch.CanonicalFrequency.Fx.ToString("R", CultureInfo.InvariantCulture))
                .Append(',').Append(notch.CanonicalFrequency.Fy.ToString("R", CultureInfo.InvariantCulture))
                .Append(',').Append(notch.Enabled ? '1' : '0');
            if (includeOrigin) builder.Append(',').Append((int)notch.Origin);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))[..16].ToLowerInvariant();
    }
}

/// <summary>候选检测的有界、只读结果。</summary>
internal sealed class PeriodicNoiseDetectionResult
{
    private readonly PeriodicFrequencyCandidate[] _candidates;
    private readonly PeriodicNotch[] _suggestions;

    public PeriodicNoiseDetectionResult(IEnumerable<PeriodicFrequencyCandidate> candidates,
        IEnumerable<PeriodicNotch> suggestions)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(suggestions);
        _candidates = candidates.Take(65).ToArray();
        _suggestions = suggestions.Take(13).ToArray();
        if (_candidates.Length > 64 || _suggestions.Length > 12)
            throw new ArgumentException("候选或建议数量超出结构上限。");
    }

    public IReadOnlyList<PeriodicFrequencyCandidate> Candidates => Array.AsReadOnly(_candidates);
    public IReadOnlyList<PeriodicNotch> Suggestions => Array.AsReadOnly(_suggestions);
}
