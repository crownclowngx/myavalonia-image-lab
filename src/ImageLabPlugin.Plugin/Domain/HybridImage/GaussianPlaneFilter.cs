using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Domain.HybridImage;

/// <summary>生成 3σ 截断核并以 Reflect101 边界执行确定性可分离 Gaussian。</summary>
/// <remarks>
/// V1 只承诺 Gaussian，因此这里是一个 sealed 服务而不是可替换 Strategy。Reflect101 不重复边缘样本，
/// 对常量平面严格保持常量；水平、垂直两个阶段都按行检查取消，且从不修改源缓冲。
/// </remarks>
internal sealed class GaussianPlaneFilter
{
    public const long MaximumWorkItems = 450_000_000;

    public double[] CreateKernel(double sigma)
    {
        ValidateSigma(sigma);
        var radius = checked((int)Math.Ceiling(3d * sigma));
        var kernel = new double[checked((radius * 2) + 1)];
        double sum = 0d;
        for (var offset = -radius; offset <= radius; offset++)
        {
            var value = Math.Exp(-(offset * (double)offset) / (2d * sigma * sigma));
            kernel[offset + radius] = value;
            sum += value;
        }
        for (var i = 0; i < kernel.Length; i++) kernel[i] /= sum;
        return kernel;
    }

    public HybridLumaPlane Apply(HybridLumaPlane source, double sigma,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var kernel = CreateKernel(sigma);
        var radius = kernel.Length / 2;
        var work = checked(source.Size.PixelCount * kernel.Length * 2L);
        if (work > MaximumWorkItems)
            throw new InvalidOperationException($"Gaussian 工作量 {work:N0} 超过 {MaximumWorkItems:N0} 门禁。");
        var temporary = new double[checked((int)source.Size.PixelCount)];
        var output = new double[temporary.Length];
        var input = source.Values.Span;

        for (var y = 0; y < source.Size.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < source.Size.Width; x++)
            {
                double sum = 0d;
                for (var k = -radius; k <= radius; k++)
                    sum += input[(y * source.Size.Width) + Reflect101(x + k, source.Size.Width)] * kernel[k + radius];
                temporary[(y * source.Size.Width) + x] = sum;
            }
        }
        for (var y = 0; y < source.Size.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < source.Size.Width; x++)
            {
                double sum = 0d;
                for (var k = -radius; k <= radius; k++)
                    sum += temporary[(Reflect101(y + k, source.Size.Height) * source.Size.Width) + x] * kernel[k + radius];
                output[(y * source.Size.Width) + x] = sum;
            }
        }
        return new HybridLumaPlane(source.Size, output);
    }

    public static double FiftyPercentCutoff(double sigma)
    {
        ValidateSigma(sigma);
        return Math.Sqrt(Math.Log(2d)) / (Math.Sqrt(2d) * Math.PI * sigma);
    }

    internal static int Reflect101(int index, int length)
    {
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
        if (length == 1) return 0;
        while ((uint)index >= (uint)length)
            index = index < 0 ? -index : ((2 * length) - index - 2);
        return index;
    }

    private static void ValidateSigma(double sigma)
    {
        if (!double.IsFinite(sigma) || sigma is < HybridImageRecipe.MinimumSigma or > HybridImageRecipe.MaximumSigma)
            throw new ArgumentOutOfRangeException(nameof(sigma), "σ 必须是 [0.8,32] 内的有限值。");
    }
}
