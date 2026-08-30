using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Domain.Wavelets;

/// <summary>集中二维扫描、扩展和布局，具体策略只实现一维正逆步骤。</summary>
/// <remarks>
/// 二维正变换固定“逐行 X，再逐列 Y”；逆变换严格反序“逐列 Y，再逐行 X”。因此 packed
/// 左下象限是 LH（X 低通/Y 高通），右上象限是 HL（X 高通/Y 低通）。基类只消除两个策略间
/// 必然相同的编排代码，不承担阈值、图片回写、文件或 UI 职责。
/// </remarks>
internal abstract class WaveletTransformBase : IWaveletTransform
{
    public abstract WaveletTransformId Id { get; }

    public WaveletPyramid Forward(ImageChannelPlane plane, int levels, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plane);
        ValidateLevels(plane.Size, levels);
        var divisor = 1 << levels;
        var paddedWidth = RoundUp(plane.Size.Width, divisor);
        var paddedHeight = RoundUp(plane.Size.Height, divisor);
        var sampleCount = checked((long)paddedWidth * paddedHeight);
        if (sampleCount > WaveletLimits.MaximumPixels)
            throw new InvalidOperationException($"扩展后平面 {paddedWidth}×{paddedHeight} 超过 {WaveletLimits.MaximumPixels:N0} 样本预算。");

        var coefficients = new double[checked((int)sampleCount)];
        var source = plane.Values.Span;
        for (var y = 0; y < paddedHeight; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceY = MirrorIndex(y, plane.Size.Height);
            for (var x = 0; x < paddedWidth; x++)
                coefficients[(y * paddedWidth) + x] = source[(sourceY * plane.Size.Width) + MirrorIndex(x, plane.Size.Width)];
        }

        var descriptors = new WaveletLevelDescriptor[levels];
        var activeWidth = paddedWidth;
        var activeHeight = paddedHeight;
        // 一半保存列样本，另一半保存一维策略的输出，二者绝不重叠。
        var maximumLine = Math.Max(paddedWidth, paddedHeight);
        var workspace = new double[checked(maximumLine * 2)];
        for (var level = 1; level <= levels; level++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ForwardLevel(coefficients, paddedWidth, activeWidth, activeHeight, workspace, cancellationToken);
            var halfWidth = activeWidth / 2;
            var halfHeight = activeHeight / 2;
            descriptors[level - 1] = new WaveletLevelDescriptor(
                level, activeWidth, activeHeight,
                new(0, 0, halfWidth, halfHeight),
                new(0, halfHeight, halfWidth, halfHeight),
                new(halfWidth, 0, halfWidth, halfHeight),
                new(halfWidth, halfHeight, halfWidth, halfHeight));
            activeWidth = halfWidth;
            activeHeight = halfHeight;
        }

        return new WaveletPyramid(Id, plane.Channel, plane.Size, new(paddedWidth, paddedHeight), coefficients, descriptors);
    }

    public ImageChannelPlane Inverse(WaveletPyramid pyramid, CancellationToken cancellationToken = default) =>
        InverseToLevel(pyramid, 1, cancellationToken);

    public ImageChannelPlane InverseToLevel(WaveletPyramid pyramid, int targetLevel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pyramid);
        if (pyramid.Transform != Id)
            throw new ArgumentException($"{Id} 策略不能逆变换 {pyramid.Transform} 金字塔。", nameof(pyramid));
        if (targetLevel is < 1 || targetLevel > pyramid.Levels.Count)
            throw new ArgumentOutOfRangeException(nameof(targetLevel), "目标重建层超出金字塔范围。");
        var coefficients = pyramid.CloneCoefficients();
        var maximumLine = Math.Max(pyramid.PaddedSize.Width, pyramid.PaddedSize.Height);
        var workspace = new double[checked(maximumLine * 2)];
        // 从最深 LL 开始逐层撤销，直到用户选择的层。targetLevel=1 即完整逆变换。
        for (var level = pyramid.Levels.Count; level >= targetLevel; level--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var descriptor = pyramid.GetLevel(level);
            InverseLevel(coefficients, pyramid.PaddedSize.Width, descriptor.ActiveWidth, descriptor.ActiveHeight, workspace, cancellationToken);
        }

        var outputSize = targetLevel == 1
            ? pyramid.OriginalSize
            : new ImageSize(pyramid.GetLevel(targetLevel).ActiveWidth, pyramid.GetLevel(targetLevel).ActiveHeight);
        var cropped = new double[checked((int)outputSize.PixelCount)];
        for (var y = 0; y < outputSize.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Array.Copy(coefficients, y * pyramid.PaddedSize.Width, cropped, y * outputSize.Width, outputSize.Width);
        }
        return new ImageChannelPlane(outputSize, pyramid.Channel, cropped);
    }

    protected abstract void Forward1D(Span<double> values, Span<double> workspace);
    protected abstract void Inverse1D(Span<double> values, Span<double> workspace);

    private void ForwardLevel(double[] data, int stride, int width, int height, double[] workspace, CancellationToken token)
    {
        for (var y = 0; y < height; y++)
        {
            token.ThrowIfCancellationRequested();
            Forward1D(data.AsSpan(y * stride, width), workspace.AsSpan(0, width));
        }
        for (var x = 0; x < width; x++)
        {
            token.ThrowIfCancellationRequested();
            for (var y = 0; y < height; y++) workspace[y] = data[(y * stride) + x];
            Forward1D(workspace.AsSpan(0, height), workspace.AsSpan(workspace.Length / 2, height));
            for (var y = 0; y < height; y++) data[(y * stride) + x] = workspace[y];
        }
    }

    private void InverseLevel(double[] data, int stride, int width, int height, double[] workspace, CancellationToken token)
    {
        for (var x = 0; x < width; x++)
        {
            token.ThrowIfCancellationRequested();
            for (var y = 0; y < height; y++) workspace[y] = data[(y * stride) + x];
            Inverse1D(workspace.AsSpan(0, height), workspace.AsSpan(workspace.Length / 2, height));
            for (var y = 0; y < height; y++) data[(y * stride) + x] = workspace[y];
        }
        for (var y = 0; y < height; y++)
        {
            token.ThrowIfCancellationRequested();
            Inverse1D(data.AsSpan(y * stride, width), workspace.AsSpan(0, width));
        }
    }

    private static void ValidateLevels(ImageSize size, int levels)
    {
        if (levels is < 1 or > WaveletLimits.MaximumLevels)
            throw new ArgumentOutOfRangeException(nameof(levels), $"分解层数必须位于 1–{WaveletLimits.MaximumLevels}。");
        if (size.Width < 1 || size.Height < 1) throw new ArgumentException("分析平面不能为空。", nameof(size));
    }

    private static int RoundUp(int value, int divisor) => checked(((value + divisor - 1) / divisor) * divisor);

    /// <summary>重复端点的对称扩展：例如 [a,b,c] 向右得到 [a,b,c,c,b,a,…]。</summary>
    private static int MirrorIndex(int index, int length)
    {
        if (length == 1) return 0;
        var period = checked(length * 2);
        var normalized = index % period;
        return normalized < length ? normalized : period - normalized - 1;
    }
}
