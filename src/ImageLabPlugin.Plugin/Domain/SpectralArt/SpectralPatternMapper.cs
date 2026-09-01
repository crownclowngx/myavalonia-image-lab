using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ImageLabPlugin.Domain.Frequency;

namespace ImageLabPlugin.Domain.SpectralArt;

/// <summary>中心化归一化频谱中的主嵌入矩形。</summary>
internal sealed record SpectralArtRegion
{
    public SpectralArtRegion(double left, double top, double right, double bottom)
    {
        if (!double.IsFinite(left) || !double.IsFinite(top) ||
            !double.IsFinite(right) || !double.IsFinite(bottom))
            throw new ArgumentOutOfRangeException(nameof(left), "频谱区域坐标必须有限。");
        if (left < -0.5d || right > 0.5d || top < -0.5d || bottom > 0.5d ||
            left >= right || top >= bottom)
            throw new ArgumentOutOfRangeException(nameof(left), "频谱区域必须是 [-0.5,0.5] 内的非空闭开矩形。");
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    public double Left { get; }
    public double Top { get; }
    public double Right { get; }
    public double Bottom { get; }

    public static SpectralArtRegion Default { get; } = new(0.14d, -0.34d, 0.34d, -0.14d);
}

/// <summary>Pattern 在主区域与中心共轭副本上的确定性映射。</summary>
internal sealed class SpectralPatternMapping
{
    private readonly double[] _mainWeights;

    public SpectralPatternMapping(
        int spectrumWidth,
        int spectrumHeight,
        int left,
        int top,
        int width,
        int height,
        ReadOnlySpan<double> mainWeights,
        int nonZeroBins,
        int foregroundBins,
        int backgroundBins,
        string fingerprint)
    {
        if (mainWeights.Length != checked(width * height))
            throw new ArgumentException("映射权重长度与主区域不一致。", nameof(mainWeights));
        SpectrumWidth = spectrumWidth;
        SpectrumHeight = spectrumHeight;
        Left = left;
        Top = top;
        Width = width;
        Height = height;
        NonZeroBins = nonZeroBins;
        ForegroundBins = foregroundBins;
        BackgroundBins = backgroundBins;
        Fingerprint = fingerprint;
        _mainWeights = mainWeights.ToArray();
    }

    public int SpectrumWidth { get; }
    public int SpectrumHeight { get; }
    public int Left { get; }
    public int Top { get; }
    public int Width { get; }
    public int Height { get; }
    public int NonZeroBins { get; }
    public int ForegroundBins { get; }
    public int BackgroundBins { get; }
    public string Fingerprint { get; }
    public int MainBinCount => checked(Width * Height);
    public int TotalMappedBinCount => checked(NonZeroBins * 2);
    internal ReadOnlySpan<double> MainWeightSpan => _mainWeights;
    public double this[int localX, int localY] => _mainWeights[(localY * Width) + localX];
}

/// <summary>把不可变 Pattern 离散到合法主矩形，并保证其中心共轭副本不会重叠或触碰禁止点。</summary>
/// <remarks>
/// Region 使用闭开边界，归一化坐标以 ToEven 离散成显示索引边界。所有合法性判断都在 Domain 完成；
/// View 只提交用户意图，不能通过 clamp 把非法区域悄悄改成另一个结果。主区域必须完整位于规范半平面，
/// 每个主 bin 的共轭点由共享 FrequencyCoordinates 计算，副本权重与主图案相同且自然形成 180° 点对称。
/// </remarks>
internal sealed class SpectralPatternMapper
{
    public const double DcExclusionRadius = 0.08d;
    public const double MaximumOccupiedRatio = 0.20d;
    public const int MinimumRegionEdge = 8;

    public SpectralPatternMapping Map(
        SpectralPattern pattern,
        SpectralArtRegion region,
        SpectralPatternFitMode fitMode,
        int spectrumWidth,
        int spectrumHeight,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(region);
        if (!Enum.IsDefined(fitMode)) throw new ArgumentOutOfRangeException(nameof(fitMode));
        ValidateSpectrumDimensions(spectrumWidth, spectrumHeight);

        var left = Boundary(region.Left, spectrumWidth);
        var right = Boundary(region.Right, spectrumWidth);
        var top = Boundary(region.Top, spectrumHeight);
        var bottom = Boundary(region.Bottom, spectrumHeight);
        var width = right - left;
        var height = bottom - top;
        if (width < MinimumRegionEdge || height < MinimumRegionEdge)
            throw new InvalidOperationException("主嵌入区域离散后必须至少为 8×8 bins。");
        var area = checked(width * height);
        if (area > checked(spectrumWidth * spectrumHeight) * MaximumOccupiedRatio)
            throw new InvalidOperationException("主嵌入区域超过频谱总 bins 的 20%。");

        ValidateEveryBin(left, top, width, height, spectrumWidth, spectrumHeight, cancellationToken);
        var weights = new double[area];
        var content = ResolveContent(pattern, fitMode, width, height);
        var nonZero = 0;
        var foreground = 0;
        var background = 0;
        for (var localY = 0; localY < height; localY++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var localX = 0; localX < width; localX++)
            {
                var weight = Sample(pattern, content, localX, localY);
                weights[(localY * width) + localX] = weight;
                if (weight > 0d) nonZero++;
                if (weight >= 0.5d) foreground++;
                if (weight <= 0.05d) background++;
            }
        }

        if (nonZero == 0) throw new InvalidOperationException("Pattern 映射后没有命中任何频点。");
        var fingerprint = Fingerprint(pattern, region, fitMode, spectrumWidth, spectrumHeight, weights);
        return new SpectralPatternMapping(spectrumWidth, spectrumHeight, left, top, width, height,
            weights, nonZero, foreground, background, fingerprint);
    }

    private static int Boundary(double coordinate, int length) =>
        Math.Clamp((int)Math.Round((coordinate + 0.5d) * length, MidpointRounding.ToEven), 0, length);

    private static void ValidateEveryBin(
        int left,
        int top,
        int width,
        int height,
        int spectrumWidth,
        int spectrumHeight,
        CancellationToken cancellationToken)
    {
        for (var y = top; y < top + height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = left; x < left + width; x++)
            {
                var point = FrequencyCoordinates.FromDisplay(x, y, spectrumWidth, spectrumHeight);
                if (!(point.Fy < 0d || (point.Fy == 0d && point.Fx > 0d)))
                    throw new InvalidOperationException("主区域必须完整位于规范频率半平面。");
                if (point.Radius < DcExclusionRadius || Math.Abs(point.Kx) <= 1 || Math.Abs(point.Ky) <= 1)
                    throw new InvalidOperationException("主区域触碰 DC 或中心坐标轴保护带。");
                if (x <= 1 || y <= 1)
                    throw new InvalidOperationException("主区域触碰 Nyquist 行/列保护带。");
                var conjugate = FrequencyCoordinates.ConjugateIndex(
                    point.InternalX, point.InternalY, spectrumWidth, spectrumHeight);
                if (conjugate.X == point.InternalX && conjugate.Y == point.InternalY)
                    throw new InvalidOperationException("主区域包含自共轭频点。");
                var conjugateDisplay = FrequencyCoordinates.FromInternal(
                    conjugate.X, conjugate.Y, spectrumWidth, spectrumHeight);
                if (conjugateDisplay.DisplayX >= left && conjugateDisplay.DisplayX < left + width &&
                    conjugateDisplay.DisplayY >= top && conjugateDisplay.DisplayY < top + height)
                    throw new InvalidOperationException("主区域不能与中心共轭副本重叠。");
            }
        }
    }

    private static (int X, int Y, int Width, int Height) ResolveContent(
        SpectralPattern pattern,
        SpectralPatternFitMode fitMode,
        int width,
        int height)
    {
        if (fitMode == SpectralPatternFitMode.Stretch) return (0, 0, width, height);
        var patternRatio = pattern.Width / (double)pattern.Height;
        var regionRatio = width / (double)height;
        if (patternRatio >= regionRatio)
        {
            var contentHeight = Math.Max(1, (int)Math.Round(width / patternRatio, MidpointRounding.ToEven));
            return (0, (height - contentHeight) / 2, width, contentHeight);
        }
        var contentWidth = Math.Max(1, (int)Math.Round(height * patternRatio, MidpointRounding.ToEven));
        return ((width - contentWidth) / 2, 0, contentWidth, height);
    }

    private static double Sample(
        SpectralPattern pattern,
        (int X, int Y, int Width, int Height) content,
        int localX,
        int localY)
    {
        var x = localX - content.X;
        var y = localY - content.Y;
        if (x < 0 || y < 0 || x >= content.Width || y >= content.Height) return 0d;
        if (pattern.SamplingMode == SpectralPatternSamplingMode.BinaryNearest)
        {
            var sourceX = Math.Min(pattern.Width - 1,
                (int)Math.Floor((x + 0.5d) * pattern.Width / content.Width));
            var sourceY = Math.Min(pattern.Height - 1,
                (int)Math.Floor((y + 0.5d) * pattern.Height / content.Height));
            return pattern[sourceX, sourceY];
        }
        return SampleArea(pattern, x, y, content.Width, content.Height);
    }

    private static double SampleArea(SpectralPattern pattern, int x, int y, int width, int height)
    {
        var left = x * pattern.Width / (double)width;
        var right = (x + 1d) * pattern.Width / width;
        var top = y * pattern.Height / (double)height;
        var bottom = (y + 1d) * pattern.Height / height;
        double weighted = 0d, total = 0d;
        for (var sourceY = (int)Math.Floor(top); sourceY < Math.Ceiling(bottom); sourceY++)
        {
            var yWeight = Math.Max(0d, Math.Min(bottom, sourceY + 1d) - Math.Max(top, sourceY));
            for (var sourceX = (int)Math.Floor(left); sourceX < Math.Ceiling(right); sourceX++)
            {
                var xWeight = Math.Max(0d, Math.Min(right, sourceX + 1d) - Math.Max(left, sourceX));
                var weight = xWeight * yWeight;
                weighted += pattern[Math.Min(sourceX, pattern.Width - 1),
                    Math.Min(sourceY, pattern.Height - 1)] * weight;
                total += weight;
            }
        }
        return total <= 0d ? 0d : Math.Clamp(weighted / total, 0d, 1d);
    }

    private static string Fingerprint(
        SpectralPattern pattern,
        SpectralArtRegion region,
        SpectralPatternFitMode fitMode,
        int width,
        int height,
        double[] weights)
    {
        var canonical = string.Join('|', "spectral-mapping-v1", pattern.Fingerprint, (int)fitMode, width, height,
            region.Left.ToString("R", CultureInfo.InvariantCulture),
            region.Top.ToString("R", CultureInfo.InvariantCulture),
            region.Right.ToString("R", CultureInfo.InvariantCulture),
            region.Bottom.ToString("R", CultureInfo.InvariantCulture));
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(canonical));
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        foreach (var value in weights)
        {
            BitConverter.TryWriteBytes(bytes, BitConverter.DoubleToInt64Bits(value));
            hash.AppendData(bytes);
        }
        return Convert.ToHexString(hash.GetHashAndReset())[..16].ToLowerInvariant();
    }

    private static void ValidateSpectrumDimensions(int width, int height)
    {
        if (width <= 0 || height <= 0 || width > 2048 || height > 2048 ||
            (width & (width - 1)) != 0 || (height & (height - 1)) != 0 ||
            checked(width * height) > FrequencySpectrum.MaximumComplexValues)
            throw new ArgumentOutOfRangeException(nameof(width), "频谱尺寸不符合共享 2048² radix-2 预算。");
    }
}
