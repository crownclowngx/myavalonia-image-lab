using System.Buffers.Binary;
using System.Security.Cryptography;
using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Domain.SeamCarving;

internal enum SeamOrientation { Vertical, Horizontal }
internal enum SeamOperation { Remove, Insert }
internal enum SeamMaskValue : byte { Normal = 0, Protect = 1, PreferRemoval = 2 }
internal enum SeamBrushTool { Protect, PreferRemoval, Erase }
internal enum SeamAxisOrder { Auto, WidthFirst, HeightFirst }
internal enum ReferenceResizeAlgorithm { Bilinear, BicubicCatmullRom }
internal enum SeamPlaybackState { Empty, Ready, Planning, Paused, Playing, Completed, Canceled, Faulted, Stale }
internal enum EnergyDisplayMode { Linear, Logarithmic }

internal static class SeamCarvingProtocols
{
    public const string Energy = "seam-energy-bt601-white-matte-sobel-v1";
    public const string Interpolation = "seam-premultiplied-srgb-even-rounding-v1";
    public const string Budget = "seam-resource-budget-v1";
    public const string Plan = "seam-resize-plan-v1";
    public const string ReportSchema = "image-lab-seam-carving-report-v1";
    public const string SnapshotSchema = "image-lab-seam-carving-document-v1";
}

/// <summary>一条归一化画笔轨迹；点坐标位于 [0,1]，因此快照不依赖某个控件的物理像素。</summary>
internal sealed record SeamBrushStroke(
    SeamBrushTool Tool,
    double RadiusNormalized,
    IReadOnlyList<SeamNormalizedPoint> Points,
    int Sequence)
{
    public const int MaximumPoints = 2_048;

    public SeamBrushStroke Validate()
    {
        if (RadiusNormalized is <= 0d or > 0.25d || !double.IsFinite(RadiusNormalized))
            throw new ArgumentOutOfRangeException(nameof(RadiusNormalized), "归一化笔径必须位于 (0, 0.25]。 ");
        if (Points.Count is 0 or > MaximumPoints)
            throw new ArgumentOutOfRangeException(nameof(Points), $"一条笔划必须包含 1 至 {MaximumPoints} 个点。");
        if (Sequence < 0) throw new ArgumentOutOfRangeException(nameof(Sequence));
        foreach (var point in Points) point.Validate();
        return this;
    }
}

internal readonly record struct SeamNormalizedPoint(double X, double Y)
{
    public void Validate()
    {
        if (!double.IsFinite(X) || !double.IsFinite(Y) || X is < 0d or > 1d || Y is < 0d or > 1d)
            throw new ArgumentOutOfRangeException(nameof(X), "归一化画笔坐标必须是 [0,1] 内的有限数。");
    }
}

/// <summary>与工作图同尺寸的三态蒙版。</summary>
/// <remarks>
/// 单个 byte 同时表达普通、保护和优先删除，天然保证两种偏置不会在同一像素叠加。数组由本对象独占，
/// 对外只读；只有同领域的栅格化和缝变形服务可以通过内部入口写入。
/// </remarks>
internal sealed class SeamMask
{
    private readonly byte[] _values;

    public SeamMask(ImageSize size) : this(size, new byte[checked((int)size.PixelCount)]) { }

    public SeamMask(ImageSize size, ReadOnlySpan<byte> values)
    {
        if (values.Length != size.PixelCount) throw new ArgumentException("蒙版长度必须等于像素数。", nameof(values));
        if (values.ContainsAnyExcept((byte)SeamMaskValue.Normal, (byte)SeamMaskValue.Protect,
                (byte)SeamMaskValue.PreferRemoval))
            throw new ArgumentException("蒙版包含未知三态值。", nameof(values));
        Size = size;
        _values = values.ToArray();
    }

    private SeamMask(ImageSize size, byte[] ownedValues)
    {
        Size = size;
        _values = ownedValues;
    }

    public ImageSize Size { get; }
    public ReadOnlyMemory<byte> Values => _values;
    internal Span<byte> WritableValues => _values;
    public SeamMaskValue Get(int x, int y) => (SeamMaskValue)_values[GetIndex(x, y)];
    internal void Set(int x, int y, SeamMaskValue value) => _values[GetIndex(x, y)] = (byte)value;
    public SeamMask Clone() => new(Size, (byte[])_values.Clone());

    public (long Normal, long Protect, long PreferRemoval) CountValues()
    {
        long normal = 0, protect = 0, removal = 0;
        foreach (var value in _values)
        {
            if (value == (byte)SeamMaskValue.Normal) normal++;
            else if (value == (byte)SeamMaskValue.Protect) protect++;
            else removal++;
        }
        return (normal, protect, removal);
    }

    private int GetIndex(int x, int y)
    {
        if ((uint)x >= (uint)Size.Width || (uint)y >= (uint)Size.Height)
            throw new ArgumentOutOfRangeException(nameof(x), $"蒙版坐标 ({x},{y}) 超出 {Size.Width}×{Size.Height}。");
        return checked((y * Size.Width) + x);
    }
}

internal sealed record SeamEnergySummary(
    double Minimum, double Maximum, double Mean, double P50, double P95, long NonFiniteCount);

/// <summary>一次 Sobel 计算的基础能量和施加有限区域偏置后的有效能量。</summary>
internal sealed class SeamEnergyMap
{
    private readonly double[] _baseEnergy;
    private readonly double[] _effectiveEnergy;

    public SeamEnergyMap(ImageSize size, ReadOnlySpan<double> baseEnergy, ReadOnlySpan<double> effectiveEnergy,
        SeamEnergySummary summary)
    {
        var expected = checked((int)size.PixelCount);
        if (baseEnergy.Length != expected || effectiveEnergy.Length != expected)
            throw new ArgumentException("能量平面长度必须等于像素数。");
        if (baseEnergy.ContainsAnyExceptInRange(0d, 1d) || effectiveEnergy.ContainsAnyExceptFinite())
            throw new ArgumentException("能量平面必须是有限数，且基础能量必须位于 [0,1]。");
        Size = size;
        _baseEnergy = baseEnergy.ToArray();
        _effectiveEnergy = effectiveEnergy.ToArray();
        Summary = summary;
    }

    public ImageSize Size { get; }
    public SeamEnergySummary Summary { get; }
    public ReadOnlyMemory<double> BaseEnergy => _baseEnergy;
    public ReadOnlyMemory<double> EffectiveEnergy => _effectiveEnergy;
    public double GetBase(int x, int y) => _baseEnergy[checked((y * Size.Width) + x)];
    public double GetEffective(int x, int y) => _effectiveEnergy[checked((y * Size.Width) + x)];
}

/// <summary>经过完整尺寸、范围和 8 邻接验证的一条缝。</summary>
internal sealed class SeamPath
{
    private readonly int[] _coordinates;

    public SeamPath(SeamOrientation orientation, ImageSize sourceSize, ReadOnlySpan<int> coordinates,
        double baseEnergy, double effectiveEnergy, int protectHits, int preferRemovalHits)
    {
        var expected = orientation == SeamOrientation.Vertical ? sourceSize.Height : sourceSize.Width;
        var secondary = orientation == SeamOrientation.Vertical ? sourceSize.Width : sourceSize.Height;
        if (coordinates.Length != expected) throw new ArgumentException("缝长度与主轴长度不一致。", nameof(coordinates));
        for (var i = 0; i < coordinates.Length; i++)
        {
            if ((uint)coordinates[i] >= (uint)secondary)
                throw new ArgumentOutOfRangeException(nameof(coordinates), $"第 {i} 个缝坐标超出次轴范围。");
            if (i > 0 && Math.Abs(coordinates[i] - coordinates[i - 1]) > 1)
                throw new ArgumentException("相邻主轴位置的缝坐标差不能超过 1。", nameof(coordinates));
        }
        if (!double.IsFinite(baseEnergy) || !double.IsFinite(effectiveEnergy))
            throw new ArgumentException("缝累计能量必须是有限数。");
        if (protectHits < 0 || preferRemovalHits < 0) throw new ArgumentOutOfRangeException(nameof(protectHits));
        Orientation = orientation;
        SourceSize = sourceSize;
        _coordinates = coordinates.ToArray();
        BaseEnergy = baseEnergy;
        EffectiveEnergy = effectiveEnergy;
        ProtectHits = protectHits;
        PreferRemovalHits = preferRemovalHits;
    }

    public SeamOrientation Orientation { get; }
    public ImageSize SourceSize { get; }
    public ReadOnlyMemory<int> Coordinates => _coordinates;
    public double BaseEnergy { get; }
    public double EffectiveEnergy { get; }
    public int ProtectHits { get; }
    public int PreferRemovalHits { get; }
}

/// <summary>影子删除规划得到的批次起点坐标；应用时再根据已插入缝修正偏移。</summary>
internal sealed record SeamInsertionPath(SeamOrientation Orientation, ImageSize BatchSourceSize,
    IReadOnlyList<int> OriginalCoordinates);

internal sealed record SeamInsertionBatch(
    SeamOrientation Orientation,
    ImageSize SourceSize,
    IReadOnlyList<SeamInsertionPath> Paths,
    string Fingerprint);

internal sealed record SeamResizeRequest(
    ImageSize TargetSize,
    SeamAxisOrder AxisOrder,
    ReferenceResizeAlgorithm ReferenceAlgorithm);

internal sealed record SeamResourceEstimate(
    long MaximumWorkingPixels,
    int TotalSeams,
    double WidthChangeRatio,
    double HeightChangeRatio,
    long EstimatedCellVisits,
    long EstimatedPeakBytes,
    long PlannedCoordinateCount,
    IReadOnlyList<string> BlockingReasons)
{
    public bool IsAllowed => BlockingReasons.Count == 0;
}

internal sealed record SeamResizePlan(
    string InputFingerprint,
    string MaskFingerprint,
    ImageSize InputSize,
    SeamResizeRequest Request,
    IReadOnlyList<(SeamOrientation Orientation, SeamOperation Operation)> Steps,
    SeamResourceEstimate ResourceEstimate,
    string Fingerprint);

internal sealed record SeamStepPreview(
    int StepNumber,
    int TotalSteps,
    SeamOperation Operation,
    SeamEnergyMap Energy,
    SeamPath Path);

internal sealed record SeamStepRecord(
    int StepNumber,
    SeamOrientation Orientation,
    SeamOperation Operation,
    ImageSize BeforeSize,
    ImageSize AfterSize,
    double BaseEnergy,
    double EffectiveEnergy,
    int ProtectHits,
    int PreferRemovalHits);

internal static class SeamFingerprint
{
    /// <summary>尺寸参与散列，避免不同二维解释共享相同 RGBA 字节时发生身份碰撞。</summary>
    public static string ForImage(PixelImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        Span<byte> dimensions = stackalloc byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(dimensions, image.Size.Width);
        BinaryPrimitives.WriteInt32LittleEndian(dimensions[4..], image.Size.Height);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(dimensions);
        hash.AppendData(image.Rgba.Span);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public static string ForMask(SeamMask mask) => Convert.ToHexString(SHA256.HashData(mask.Values.Span));

    public static string ForText(string text) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
}

file static class SeamSpanValidation
{
    public static bool ContainsAnyExcept(this ReadOnlySpan<byte> values, byte first, byte second, byte third)
    {
        foreach (var value in values) if (value != first && value != second && value != third) return true;
        return false;
    }

    public static bool ContainsAnyExceptInRange(this ReadOnlySpan<double> values, double minimum, double maximum)
    {
        foreach (var value in values) if (!double.IsFinite(value) || value < minimum || value > maximum) return true;
        return false;
    }

    public static bool ContainsAnyExceptFinite(this ReadOnlySpan<double> values)
    {
        foreach (var value in values) if (!double.IsFinite(value)) return true;
        return false;
    }
}
