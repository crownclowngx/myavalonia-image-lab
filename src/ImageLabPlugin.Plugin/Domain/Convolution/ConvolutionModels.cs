using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ImageLabPlugin.Domain.Shared.Spatial;

namespace ImageLabPlugin.Domain.Convolution;

internal enum ConvolutionChannelMode { Rgb, Red, Green, Blue, Luma, ChromaBlue, ChromaRed }
internal enum GradientOutputMode { X, Y, Magnitude }
internal enum ConvolutionOperatorKind { Single, GradientPair }

/// <summary>单核或梯度双核的显式定义；稳定 ID 用于快照，显示名不参与协议。</summary>
internal sealed record ConvolutionOperatorDefinition(
    string StableId,
    string DisplayName,
    ConvolutionOperatorKind Kind,
    ConvolutionKernel PrimaryKernel,
    ConvolutionKernel? SecondaryKernel,
    KernelNormalizationMode RecommendedNormalization,
    double RecommendedBias,
    string Explanation)
{
    public static ConvolutionOperatorDefinition Single(string id, string name, ConvolutionKernel kernel,
        KernelNormalizationMode normalization, double bias, string explanation) =>
        new(id, name, ConvolutionOperatorKind.Single, kernel, null, normalization, bias, explanation);

    public static ConvolutionOperatorDefinition Pair(string id, string name, ConvolutionKernel x, ConvolutionKernel y,
        KernelNormalizationMode normalization, double bias, string explanation) =>
        new(id, name, ConvolutionOperatorKind.GradientPair, x, y, normalization, bias, explanation);
}

/// <summary>一次可重复卷积所需的全部数学事实。</summary>
internal sealed record ConvolutionRecipe(
    ConvolutionOperatorDefinition Operator,
    BorderDefinition Border,
    KernelNormalizationDefinition Normalization,
    double Bias,
    ConvolutionChannelMode Channel,
    GradientOutputMode GradientOutput = GradientOutputMode.Magnitude)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Operator);
        ArgumentNullException.ThrowIfNull(Border);
        ArgumentNullException.ThrowIfNull(Normalization);
        Border.Validate();
        if (!double.IsFinite(Bias) || Bias is < -4096d or > 4096d)
            throw new ArgumentOutOfRangeException(nameof(Bias), "偏置必须有限且位于 -4096 至 4096。");
        if (Operator.Kind == ConvolutionOperatorKind.GradientPair && Operator.SecondaryKernel is null)
            throw new InvalidOperationException("梯度双核必须同时提供 X 与 Y 核。");
        _ = ConvolutionNormalizer.ResolveDivisor(Operator.PrimaryKernel, Normalization);
        if (Operator.SecondaryKernel is not null)
            _ = ConvolutionNormalizer.ResolveDivisor(Operator.SecondaryKernel, Normalization);
    }

    /// <summary>指纹只编码数学参数，不编码中文显示名，因而可用于拒绝过期完整尺寸结果。</summary>
    public string Fingerprint()
    {
        Validate();
        var builder = new StringBuilder();
        builder.Append("true-convolution-v1|").Append(Operator.StableId).Append('|')
            .Append((int)Border.Mode).Append('|').Append(Border.ConstantValue.ToString("R", CultureInfo.InvariantCulture)).Append('|')
            .Append((int)Normalization.Mode).Append('|').Append(Normalization.ExplicitDivisor.ToString("R", CultureInfo.InvariantCulture)).Append('|')
            .Append(Bias.ToString("R", CultureInfo.InvariantCulture)).Append('|').Append((int)Channel).Append('|').Append((int)GradientOutput);
        AppendKernel(builder, Operator.PrimaryKernel);
        if (Operator.SecondaryKernel is not null) AppendKernel(builder, Operator.SecondaryKernel);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static void AppendKernel(StringBuilder builder, ConvolutionKernel kernel)
    {
        builder.Append('|').Append(kernel.Size);
        foreach (var coefficient in kernel.CoefficientSpan)
            builder.Append('|').Append(coefficient.ToString("R", CultureInfo.InvariantCulture));
    }
}
