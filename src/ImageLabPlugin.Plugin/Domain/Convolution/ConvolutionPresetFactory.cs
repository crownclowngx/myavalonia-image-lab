using ImageLabPlugin.Domain.Shared.Spatial;

namespace ImageLabPlugin.Domain.Convolution;

/// <summary>以显式分支生成有限 V1 预设，不通过反射发现“滤镜”。</summary>
/// <remarks>
/// Factory 只负责公式和推荐值，不读取图片也不执行卷积。这样新增预设不会复制空间循环，用户把矩阵
/// 转为自定义后也能继续由同一执行器处理。Gaussian 在工厂内先归一化到和为 1，Unsharp 与 High Boost
/// 才能直接满足各自的 DC 公式。
/// </remarks>
internal sealed class ConvolutionPresetFactory
{
    public static IReadOnlyList<string> StableIds { get; } =
        ["identity", "mean", "gaussian", "motion", "sharpen", "unsharp", "high-boost", "sobel", "prewitt", "scharr", "laplacian-4", "laplacian-8", "emboss"];

    public ConvolutionOperatorDefinition Create(string stableId, int size = 3, double sigma = 1d,
        double amount = 1d, double highBoostA = 2d, double motionLength = 3d, double angleDegrees = 0d,
        double embossStrength = 1d)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stableId);
        return stableId switch
        {
            "identity" => Single(stableId, "单位核", Identity(size), KernelNormalizationMode.None, 0, "保持输入，用于方向与数值基线。"),
            "mean" => Single(stableId, "均值", Mean(size), KernelNormalizationMode.KernelSum, 0, "等权低通；归一化后 DC 增益为 1。"),
            "gaussian" => Single(stableId, "高斯", Gaussian(size, sigma), KernelNormalizationMode.KernelSum, 0, "对称非负低通；sigma 控制扩散范围。"),
            "motion" => Single(stableId, "运动模糊", Motion(size, motionLength, angleDegrees), KernelNormalizationMode.KernelSum, 0, "把中心线段双线性栅格化为确定性非负权重。"),
            "sharpen" => Single(stableId, "锐化", Sharpen(size, amount), KernelNormalizationMode.None, 0, "中心增强、四邻域抑制；增强局部变化但不恢复丢失细节。"),
            "unsharp" => Single(stableId, "反锐化掩模", Unsharp(size, sigma, amount), KernelNormalizationMode.None, 0, "(1+a)δ-aG，核和为 1；a=0 精确退化为单位核。"),
            "high-boost" => Single(stableId, "高提升", HighBoost(size, sigma, highBoostA), KernelNormalizationMode.None, 0, "Aδ-G，DC 增益为 A-1。"),
            "sobel" => Gradient(stableId, "Sobel", [-1, 0, 1, -2, 0, 2, -1, 0, 1], [-1, -2, -1, 0, 0, 0, 1, 2, 1]),
            "prewitt" => Gradient(stableId, "Prewitt", [-1, 0, 1, -1, 0, 1, -1, 0, 1], [-1, -1, -1, 0, 0, 0, 1, 1, 1]),
            "scharr" => Gradient(stableId, "Scharr", [-3, 0, 3, -10, 0, 10, -3, 0, 3], [-3, -10, -3, 0, 0, 0, 3, 10, 3]),
            "laplacian-4" => Single(stableId, "Laplacian 四邻域", new(3, [0, 1, 0, 1, -4, 1, 0, 1, 0]), KernelNormalizationMode.None, 128, "零和二阶差分；常量区响应为 0。"),
            "laplacian-8" => Single(stableId, "Laplacian 八邻域", new(3, [1, 1, 1, 1, -8, 1, 1, 1, 1]), KernelNormalizationMode.None, 128, "八邻域零和二阶差分；建议偏置仅用于显示负响应。"),
            "emboss" => Single(stableId, "浮雕", Emboss(embossStrength, angleDegrees), KernelNormalizationMode.None, 128, "方向一阶差分；零和核建议用 128 显示正负起伏。"),
            _ => throw new ArgumentOutOfRangeException(nameof(stableId), stableId, "未知卷积预设稳定 ID。")
        };
    }

    public ConvolutionOperatorDefinition Custom(ConvolutionKernel kernel) =>
        Single("custom", "自定义", kernel, KernelNormalizationMode.None, 0, "用户显式输入的中心锚点真卷积核。");

    public ConvolutionKernel Identity(int size)
    {
        ValidateSize(size); var coefficients = new double[size * size]; coefficients[coefficients.Length / 2] = 1;
        return new ConvolutionKernel(size, coefficients);
    }

    public ConvolutionKernel Mean(int size)
    {
        ValidateSize(size); return new ConvolutionKernel(size, Enumerable.Repeat(1d, size * size).ToArray());
    }

    public ConvolutionKernel Gaussian(int size, double sigma)
    {
        ValidateSize(size); ValidateRange(sigma, 0.1, 5, nameof(sigma));
        var radius = size / 2;
        if (radius < Math.Ceiling(sigma))
            throw new ArgumentOutOfRangeException(nameof(size), "所选尺寸至少应覆盖一个 sigma 半径；请显式增大核或减小 sigma。");
        var coefficients = new double[size * size]; double sum = 0;
        for (var row = 0; row < size; row++)
            for (var column = 0; column < size; column++)
            {
                var x = column - radius; var y = row - radius;
                var value = Math.Exp(-((x * x) + (y * y)) / (2d * sigma * sigma));
                coefficients[(row * size) + column] = value; sum += value;
            }
        for (var index = 0; index < coefficients.Length; index++) coefficients[index] /= sum;
        return new ConvolutionKernel(size, coefficients);
    }

    /// <summary>
    /// 连续线段用等距样本投到相邻四个像素，实际权重是可审查的离散近似，并非声称精确面积积分。
    /// 样本数与长度/尺寸确定，重复生成严格一致；角度按 180° 周期规范化。
    /// </summary>
    public ConvolutionKernel Motion(int size, double length, double angleDegrees)
    {
        ValidateSize(size); ValidateRange(length, 1, size, nameof(length));
        if (!double.IsFinite(angleDegrees)) throw new ArgumentOutOfRangeException(nameof(angleDegrees), "角度必须有限。");
        var angle = ((angleDegrees % 180d) + 180d) % 180d * Math.PI / 180d;
        var radius = size / 2; var values = new double[size * size];
        var samples = Math.Max(8, (int)Math.Ceiling(length * 8));
        for (var index = 0; index < samples; index++)
        {
            var t = samples == 1 ? 0d : ((index / (double)(samples - 1)) - 0.5d) * (length - 1d);
            var x = radius + (Math.Cos(angle) * t); var y = radius + (Math.Sin(angle) * t);
            var x0 = (int)Math.Floor(x); var y0 = (int)Math.Floor(y); var fx = x - x0; var fy = y - y0;
            Add(values, size, x0, y0, (1 - fx) * (1 - fy)); Add(values, size, x0 + 1, y0, fx * (1 - fy));
            Add(values, size, x0, y0 + 1, (1 - fx) * fy); Add(values, size, x0 + 1, y0 + 1, fx * fy);
        }
        var sum = values.Sum(); for (var index = 0; index < values.Length; index++) values[index] /= sum;
        return new ConvolutionKernel(size, values);
    }

    public ConvolutionKernel Sharpen(int size, double amount)
    {
        ValidateSize(size); ValidateRange(amount, 0, 5, nameof(amount));
        var values = Identity(size).CoefficientSpan.ToArray(); var center = values.Length / 2;
        values[center] += 4 * amount;
        values[center - 1] -= amount; values[center + 1] -= amount;
        values[center - size] -= amount; values[center + size] -= amount;
        return new ConvolutionKernel(size, values);
    }

    public ConvolutionKernel Unsharp(int size, double sigma, double amount)
    {
        ValidateRange(amount, 0, 5, nameof(amount)); if (amount == 0) return Identity(size);
        var gaussian = Gaussian(size, sigma); var values = gaussian.CoefficientSpan.ToArray();
        for (var i = 0; i < values.Length; i++) values[i] *= -amount;
        values[values.Length / 2] += 1 + amount;
        return new ConvolutionKernel(size, values);
    }

    public ConvolutionKernel HighBoost(int size, double sigma, double a)
    {
        ValidateRange(a, 1, 6, nameof(a)); var gaussian = Gaussian(size, sigma); var values = gaussian.CoefficientSpan.ToArray();
        for (var i = 0; i < values.Length; i++) values[i] = -values[i];
        values[values.Length / 2] += a;
        return new ConvolutionKernel(size, values);
    }

    public ConvolutionKernel Emboss(double strength, double angleDegrees)
    {
        ValidateRange(strength, 0, 5, nameof(strength));
        if (!double.IsFinite(angleDegrees)) throw new ArgumentOutOfRangeException(nameof(angleDegrees), "角度必须有限。");
        var sector = ((int)Math.Round((((angleDegrees % 360) + 360) % 360) / 45d)) % 8;
        var kernel = new ConvolutionKernel(3, [-strength, -strength, 0, -strength, 0, strength, 0, strength, strength]);
        for (var turn = 0; turn < sector / 2; turn++) kernel = kernel.RotateClockwise();
        return sector % 2 == 0 ? kernel : new ConvolutionKernel(3, [-strength, 0, strength, -strength, 0, strength, -strength, 0, strength]).RotateClockwise();
    }

    private static ConvolutionOperatorDefinition Gradient(string id, string name, double[] x, double[] y) =>
        ConvolutionOperatorDefinition.Pair(id, name, new(3, x), new(3, y), KernelNormalizationMode.None, 0,
            "X/Y 是零和一阶差分；Magnitude=sqrt(Gx²+Gy²) 是非线性双核组合，不存在等价单核。");
    private static ConvolutionOperatorDefinition Single(string id, string name, ConvolutionKernel kernel,
        KernelNormalizationMode mode, double bias, string explanation) =>
        ConvolutionOperatorDefinition.Single(id, name, kernel, mode, bias, explanation);
    private static void ValidateSize(int size) { _ = new ConvolutionKernel(size, new double[checked(size * size)]); }
    private static void ValidateRange(double value, double minimum, double maximum, string name)
    { if (!double.IsFinite(value) || value < minimum || value > maximum) throw new ArgumentOutOfRangeException(name, $"参数必须位于 {minimum} 至 {maximum}。"); }
    private static void Add(double[] values, int size, int x, int y, double value)
    { if ((uint)x < (uint)size && (uint)y < (uint)size) values[(y * size) + x] += value; }
}
