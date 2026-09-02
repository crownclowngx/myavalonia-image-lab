using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ImageLabPlugin.Domain.MagnitudePhaseSwap;

internal enum MagnitudeComponentMode { SourceA, SourceB, LinearAtoB, UnitNonZero }
internal enum PhaseComponentMode { SourceA, SourceB, ShortestArcAtoB, Zero }
internal enum MagnitudePhaseProjectionKind { PhysicalClamp, SignedScientific }

/// <summary>一次幅度/相位实验的完整、不可变且始终合法的强类型配方。</summary>
/// <remarks>
/// 构造函数一次性拒绝无意义参数组合，调用链因此不需要在混合器、Document 和导出器中分别猜测默认值。
/// 固定算法版本进入指纹；未来若画布、相位或投影语义变化，必须提升协议而不能静默重解释旧结果。
/// </remarks>
internal sealed record MagnitudePhaseRecipe
{
    public const string AlgorithmVersion = "magnitude-phase-swap-v1";

    public MagnitudePhaseRecipe(int canvasSize, MagnitudeComponentMode magnitudeMode, double magnitudeAmount,
        PhaseComponentMode phaseMode, double phaseAmount, MagnitudePhaseProjectionKind projectionKind)
    {
        MagnitudePhaseCanvasSize.Validate(canvasSize);
        if (!Enum.IsDefined(magnitudeMode) || !Enum.IsDefined(phaseMode) || !Enum.IsDefined(projectionKind))
            throw new ArgumentOutOfRangeException(nameof(magnitudeMode), "配方包含未知枚举值。");
        ValidateAmount(magnitudeAmount, magnitudeMode == MagnitudeComponentMode.LinearAtoB, nameof(magnitudeAmount));
        ValidateAmount(phaseAmount, phaseMode == PhaseComponentMode.ShortestArcAtoB, nameof(phaseAmount));
        var legalModes = (magnitudeMode, phaseMode) switch
        {
            (MagnitudeComponentMode.SourceA or MagnitudeComponentMode.SourceB,
                PhaseComponentMode.SourceA or PhaseComponentMode.SourceB or PhaseComponentMode.Zero or PhaseComponentMode.ShortestArcAtoB) => true,
            (MagnitudeComponentMode.LinearAtoB, PhaseComponentMode.SourceA or PhaseComponentMode.SourceB) => true,
            (MagnitudeComponentMode.UnitNonZero, PhaseComponentMode.SourceA or PhaseComponentMode.SourceB) => true,
            _ => false
        };
        if (!legalModes) throw new ArgumentException("V1 不允许同时插值幅度和相位，也不允许无意义的单分量组合。");
        if ((magnitudeMode == MagnitudeComponentMode.UnitNonZero) !=
            (projectionKind == MagnitudePhaseProjectionKind.SignedScientific))
            throw new ArgumentException("只有 phase-only 使用 unit-nonzero 幅度与固定科学投影。");
        CanvasSize = canvasSize;
        MagnitudeMode = magnitudeMode;
        MagnitudeAmount = magnitudeAmount;
        PhaseMode = phaseMode;
        PhaseAmount = phaseAmount;
        ProjectionKind = projectionKind;
    }

    public int CanvasSize { get; }
    public MagnitudeComponentMode MagnitudeMode { get; }
    public double MagnitudeAmount { get; }
    public PhaseComponentMode PhaseMode { get; }
    public double PhaseAmount { get; }
    public MagnitudePhaseProjectionKind ProjectionKind { get; }

    public string Fingerprint()
    {
        var text = string.Join('|', AlgorithmVersion, CanvasSize,
            MagnitudeMode, MagnitudeAmount.ToString("R", CultureInfo.InvariantCulture),
            PhaseMode, PhaseAmount.ToString("R", CultureInfo.InvariantCulture), ProjectionKind);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..24].ToLowerInvariant();
    }

    private static void ValidateAmount(double value, bool used, string name)
    {
        if (!double.IsFinite(value) || value is < 0d or > 1d)
            throw new ArgumentOutOfRangeException(name, "插值参数必须是 [0,1] 内的有限值。");
        if (!used && value != 0d) throw new ArgumentException("非插值模式的插值参数必须为 0。", name);
    }
}
