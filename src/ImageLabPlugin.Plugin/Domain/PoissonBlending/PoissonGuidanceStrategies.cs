using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Domain.PoissonBlending;

/// <summary>一条有向 4 邻边的 guidance；RGB 模式使用三个分量，单色模式只使用 C0。</summary>
internal readonly record struct PoissonGuidance(double C0, double C1, double C2, int ChannelCount, bool SelectedSource)
{
    public bool IsFinite => double.IsFinite(C0) && double.IsFinite(C1) && double.IsFinite(C2) && ChannelCount is 1 or 3;
    public double Get(int channel) => channel switch
    { 0 => C0, 1 when ChannelCount == 3 => C1, 2 when ChannelCount == 3 => C2, _ => throw new ArgumentOutOfRangeException(nameof(channel)) };
}

/// <summary>
/// guidance 是产品唯一真实算法变化点。策略只接收一条边两端的线性源/目标颜色，既不知道坐标存储，
/// 也不知道问题、迭代、Session 或 UI，从而让求解器对新增 guidance 模式保持关闭。
/// </summary>
internal interface IPoissonGuidanceStrategy
{
    PoissonBlendMode Mode { get; }
    int ChannelCount { get; }
    PoissonGuidance Evaluate(LinearRgbColor sourceP, LinearRgbColor sourceQ,
        LinearRgbColor targetP, LinearRgbColor targetQ);
}

internal sealed class NormalCloneGuidanceStrategy : IPoissonGuidanceStrategy
{
    public PoissonBlendMode Mode => PoissonBlendMode.NormalClone;
    public int ChannelCount => 3;

    public PoissonGuidance Evaluate(LinearRgbColor sourceP, LinearRgbColor sourceQ,
        LinearRgbColor targetP, LinearRgbColor targetQ)
    {
        Validate(sourceP, sourceQ, targetP, targetQ);
        return new(sourceP.Red - sourceQ.Red, sourceP.Green - sourceQ.Green, sourceP.Blue - sourceQ.Blue, 3, true);
    }

    internal static void Validate(params LinearRgbColor[] colors)
    { if (colors.Any(color => !color.IsFinite)) throw new ArgumentException("guidance 输入必须为有限线性 RGB。 "); }
}

internal sealed class MixedGradientGuidanceStrategy : IPoissonGuidanceStrategy
{
    public PoissonBlendMode Mode => PoissonBlendMode.MixedGradient;
    public int ChannelCount => 3;

    public PoissonGuidance Evaluate(LinearRgbColor sourceP, LinearRgbColor sourceQ,
        LinearRgbColor targetP, LinearRgbColor targetQ)
    {
        NormalCloneGuidanceStrategy.Validate(sourceP, sourceQ, targetP, targetQ);
        var sr = sourceP.Red - sourceQ.Red; var sg = sourceP.Green - sourceQ.Green; var sb = sourceP.Blue - sourceQ.Blue;
        var tr = targetP.Red - targetQ.Red; var tg = targetP.Green - targetQ.Green; var tb = targetP.Blue - targetQ.Blue;
        // 必须按整条 RGB 向量的平方模择强。逐通道拼接会制造任一输入都不存在的颜色方向；完全平局固定选源。
        var sourceNorm = (sr * sr) + (sg * sg) + (sb * sb);
        var targetNorm = (tr * tr) + (tg * tg) + (tb * tb);
        return sourceNorm >= targetNorm ? new(sr, sg, sb, 3, true) : new(tr, tg, tb, 3, false);
    }
}

internal sealed class MonochromeGuidanceStrategy : IPoissonGuidanceStrategy
{
    public PoissonBlendMode Mode => PoissonBlendMode.Monochrome;
    public int ChannelCount => 1;

    public PoissonGuidance Evaluate(LinearRgbColor sourceP, LinearRgbColor sourceQ,
        LinearRgbColor targetP, LinearRgbColor targetQ)
    {
        NormalCloneGuidanceStrategy.Validate(sourceP, sourceQ, targetP, targetQ);
        return new(Luma(sourceP) - Luma(sourceQ), 0d, 0d, 1, true);
    }

    /// <summary>线性 BT.709 亮度；权重和为 1，未裁剪时同量 RGB delta 会精确改变该亮度。</summary>
    internal static double Luma(LinearRgbColor color) =>
        (0.2126d * color.Red) + (0.7152d * color.Green) + (0.0722d * color.Blue);
}

/// <summary>按稳定枚举提供唯一 Strategy；重复/缺失模式在组合期立即失败，不使用反射或抽象工厂。</summary>
internal sealed class PoissonGuidanceCatalog
{
    private readonly IReadOnlyDictionary<PoissonBlendMode, IPoissonGuidanceStrategy> _strategies;

    public PoissonGuidanceCatalog(IEnumerable<IPoissonGuidanceStrategy> strategies)
    {
        ArgumentNullException.ThrowIfNull(strategies);
        var groups = strategies.GroupBy(item => item.Mode).ToArray();
        if (groups.Any(group => group.Count() != 1) || groups.Length != Enum.GetValues<PoissonBlendMode>().Length)
            throw new InvalidOperationException("每种 Poisson guidance 模式必须且只能登记一个 Strategy。 ");
        _strategies = groups.ToDictionary(group => group.Key, group => group.Single());
    }

    public IPoissonGuidanceStrategy Resolve(PoissonBlendMode mode) =>
        _strategies.TryGetValue(mode, out var strategy) ? strategy : throw new ArgumentOutOfRangeException(nameof(mode));
}
