using System.Numerics;
using ImageLabPlugin.Domain.Shared.Spectral;
using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Domain.Shared.Spatial;

namespace ImageLabPlugin.Domain.Convolution;

internal sealed record KernelFrequencyResponse(
    PixelImage MagnitudeImage,
    PixelImage PhaseImage,
    ReadOnlyMemory<double> Magnitudes,
    ReadOnlyMemory<double> Phases,
    ReadOnlyMemory<double> HorizontalSection,
    ReadOnlyMemory<double> VerticalSection,
    double DcGain,
    double MaximumMagnitude,
    int MaximumX,
    int MaximumY,
    bool IsGradientSummary);

/// <summary>分析应用归一化后的核频率响应，固定使用 256×256 周期网格。</summary>
/// <remarks>
/// 核以中心为数学原点，但数组原点在左上角，所以先把 <c>(kx,ky)</c> 搬到周期网格的
/// <c>(kx mod N,ky mod N)</c>，再执行 FFT。显示时用半尺寸循环移位把 DC 搬到图中央。
/// 偏置、边界扩展和字节裁切不是线性时不变核的一部分，绝不写进 H(u,v)。双核 Magnitude 仅显示
/// <c>sqrt(|Hx|²+|Hy|²)</c> 幅值摘要，它不伪装成空间域非线性组合的等价单核。
/// </remarks>
internal sealed class KernelFrequencyResponseAnalyzer(Fft2DTransform transform)
{
    public const int GridSize = 256;

    public KernelFrequencyResponse Analyze(ConvolutionRecipe recipe, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipe); recipe.Validate();
        var primary = Transform(recipe.Operator.PrimaryKernel, recipe.Normalization, cancellationToken);
        var secondary = recipe.Operator.Kind == ConvolutionOperatorKind.GradientPair
            ? Transform(recipe.Operator.SecondaryKernel!, recipe.Normalization, cancellationToken) : null;
        var magnitudes = new double[primary.Length]; var phases = new double[primary.Length];
        double maximum = 0; var maximumIndex = 0;
        for (var index = 0; index < primary.Length; index++)
        {
            if ((index & 8191) == 0) cancellationToken.ThrowIfCancellationRequested();
            var shiftedIndex = ShiftedIndex(index);
            var primaryMagnitude = primary[index].Magnitude;
            var magnitude = secondary is null ? primaryMagnitude : Math.Sqrt(
                (primaryMagnitude * primaryMagnitude) + (secondary[index].Magnitude * secondary[index].Magnitude));
            magnitudes[shiftedIndex] = magnitude;
            phases[shiftedIndex] = secondary is null ? primary[index].Phase : 0d;
            if (magnitude > maximum) { maximum = magnitude; maximumIndex = shiftedIndex; }
        }
        var center = GridSize / 2;
        var horizontal = magnitudes.AsSpan(center * GridSize, GridSize).ToArray();
        var vertical = Enumerable.Range(0, GridSize).Select(y => magnitudes[(y * GridSize) + center]).ToArray();
        var primaryDc = primary[0].Magnitude;
        var dc = secondary is null ? primaryDc : Math.Sqrt(
            (primaryDc * primaryDc) + (secondary[0].Magnitude * secondary[0].Magnitude));
        return new KernelFrequencyResponse(ProjectMagnitude(magnitudes, maximum), ProjectPhase(phases), magnitudes, phases,
            horizontal, vertical, dc, maximum, maximumIndex % GridSize, maximumIndex / GridSize, secondary is not null);
    }

    private Complex[] Transform(ConvolutionKernel kernel, KernelNormalizationDefinition normalization, CancellationToken token)
    {
        var divisor = ConvolutionNormalizer.ResolveDivisor(kernel, normalization);
        var values = new Complex[GridSize * GridSize];
        for (var row = 0; row < kernel.Size; row++)
            for (var column = 0; column < kernel.Size; column++)
            {
                var kx = column - kernel.Radius; var ky = row - kernel.Radius;
                var x = PositiveModulo(kx, GridSize); var y = PositiveModulo(ky, GridSize);
                values[(y * GridSize) + x] = kernel[row, column] / divisor;
            }
        transform.Forward(values, GridSize, GridSize, token);
        return values;
    }

    private static int ShiftedIndex(int unshiftedIndex)
    {
        var x = unshiftedIndex % GridSize; var y = unshiftedIndex / GridSize;
        return ((((y + (GridSize / 2)) % GridSize) * GridSize) + ((x + (GridSize / 2)) % GridSize));
    }

    private static PixelImage ProjectMagnitude(double[] values, double maximum)
    {
        var rgba = new byte[values.Length * 4]; var scale = Math.Log(1d + maximum);
        for (var i = 0; i < values.Length; i++)
        {
            var value = scale <= 0 ? (byte)0 : (byte)Math.Clamp(Math.Round(255 * Math.Log(1d + values[i]) / scale), 0, 255);
            rgba[i * 4] = value; rgba[(i * 4) + 1] = value; rgba[(i * 4) + 2] = value; rgba[(i * 4) + 3] = 255;
        }
        return new PixelImage(new ImageSize(GridSize, GridSize), rgba);
    }

    private static PixelImage ProjectPhase(double[] values)
    {
        var rgba = new byte[values.Length * 4];
        for (var i = 0; i < values.Length; i++)
        {
            var unit = (values[i] + Math.PI) / (2 * Math.PI); var red = (byte)Math.Clamp(Math.Round(unit * 255), 0, 255);
            rgba[i * 4] = red; rgba[(i * 4) + 1] = (byte)(255 - red); rgba[(i * 4) + 2] = 128; rgba[(i * 4) + 3] = 255;
        }
        return new PixelImage(new ImageSize(GridSize, GridSize), rgba);
    }

    private static int PositiveModulo(int value, int modulus) => ((value % modulus) + modulus) % modulus;
}
