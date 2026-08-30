using System.Security.Cryptography;
using System.Text;
using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Domain.Steganography;

internal enum LsbPayloadKind : byte { Utf8Text = 1, Binary = 2 }
internal enum LsbChannelStrategy { Red, Green, Blue, RgbRoundRobin }
internal enum LsbChannel { Red, Green, Blue }
internal enum LsbPlacementKind { Sequential, PseudoRandom }
internal enum LsbStatisticsScope { EligibleImage, SelectedSlots, SequentialPrefix }

/// <summary>LSB V1 的结构化读取状态；调用方不能把失败时读到的字节当成成功载荷。</summary>
internal enum LsbReadStatus
{
    Success,
    InsufficientSlots,
    MagicMismatch,
    UnsupportedVersion,
    UnsupportedFlags,
    UnknownPayloadKind,
    HeaderCrcMismatch,
    LengthOutOfRange,
    PayloadCrcMismatch,
    InvalidUtf8
}

/// <summary>拥有原始载荷字节的短生命周期值对象。</summary>
/// <remarks>最大长度固定为 64 KiB；释放时清零私有副本，避免 Document 换图后仍长期保留二进制载荷。</remarks>
internal sealed class LsbPayload : IDisposable
{
    public const int MaximumBytes = 65_536;
    private byte[] _bytes;
    private bool _disposed;

    public LsbPayload(LsbPayloadKind kind, ReadOnlySpan<byte> bytes)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (bytes.Length > MaximumBytes) throw new ArgumentOutOfRangeException(nameof(bytes), "LSB V1 Payload 不能超过 65,536 字节。");
        Kind = kind;
        _bytes = bytes.ToArray();
    }

    public LsbPayloadKind Kind { get; }
    public ReadOnlyMemory<byte> Bytes
    {
        get { ThrowIfDisposed(); return _bytes; }
    }

    public static LsbPayload FromText(string text) => new(LsbPayloadKind.Utf8Text, new UTF8Encoding(false, true).GetBytes(text ?? string.Empty));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CryptographicOperations.ZeroMemory(_bytes);
        _bytes = [];
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LsbPayload));
    }
}

/// <summary>决定通道、位平面和槽位顺序的显式实验配方。</summary>
/// <remarks>seed 是公开的复现实验参数，不是密码或密钥；更改任一字段都必须使旧结果过期。</remarks>
internal readonly record struct LsbRecipe(
    LsbChannelStrategy Channels,
    int BitPlane,
    LsbPlacementKind Placement,
    ulong Seed)
{
    public const string PlacementVersion = "splitmix64-sparse-fisher-yates-v1";

    public void Validate()
    {
        if (!Enum.IsDefined(Channels)) throw new ArgumentOutOfRangeException(nameof(Channels));
        if (BitPlane is not (0 or 1)) throw new ArgumentOutOfRangeException(nameof(BitPlane), "LSB V1 只允许 bit 0 或 bit 1。");
        if (!Enum.IsDefined(Placement)) throw new ArgumentOutOfRangeException(nameof(Placement));
    }

    public int ChannelCount => Channels == LsbChannelStrategy.RgbRoundRobin ? 3 : 1;
    public string StableId => $"{Channels switch { LsbChannelStrategy.Red => "r", LsbChannelStrategy.Green => "g", LsbChannelStrategy.Blue => "b", _ => "rgb" }}-bit{BitPlane}-{(Placement == LsbPlacementKind.Sequential ? "sequential-v1" : PlacementVersion)}";
}

internal readonly record struct LsbSlot(int LogicalIndex, int PixelIndex, LsbChannel Channel, int RgbaOffset);

internal sealed record LsbFrameHeader(LsbPayloadKind PayloadKind, int PayloadLength, uint PayloadCrc32);

/// <summary>包含读取状态与受控载荷副本的结果；只有 <see cref="LsbReadStatus.Success"/> 才能使用 Payload。</summary>
internal sealed record LsbExtractionResult(
    LsbReadStatus Status,
    LsbFrameHeader? Header,
    byte[]? Payload,
    byte[] ReadFrame,
    string Explanation)
{
    public string? DecodeTextStrict()
    {
        if (Status != LsbReadStatus.Success || Header?.PayloadKind != LsbPayloadKind.Utf8Text || Payload is null) return null;
        return new UTF8Encoding(false, true).GetString(Payload);
    }
}

internal sealed record LsbCapacity(
    long OpaquePixelCount,
    long EligibleSlots,
    long FrameCapacityBytes,
    long PayloadCapacityBytes,
    int EffectivePayloadLimitBytes,
    long RequiredBits,
    double BitsPerPixel,
    double BitsPerSlot,
    bool Fits);

internal sealed record LsbChangeCell(int Column, int Row, long SelectedSlots, long ChangedSlots);

/// <summary>写入器返回的不可变事实；位置用紧凑 int 数组表示，不为每个槽位创建对象。</summary>
internal sealed record LsbEmbeddingFacts(
    int FrameBits,
    int HeaderBits,
    int PayloadBits,
    int[] SelectedLogicalSlots,
    long ChangedSlots,
    long UnchangedSlots,
    long NegativeChanges,
    long PositiveChanges,
    IReadOnlyDictionary<LsbChannel, long> ChangedByChannel,
    IReadOnlyList<LsbChangeCell> ChangeGrid,
    double MseRgb,
    double? PsnrRgbDb);

internal sealed record LsbEmbeddingResult(PixelImage Image, LsbEmbeddingFacts Facts);

internal enum LsbProbeSelectionState { NotEligible, NotSelected, HeaderUnchanged, HeaderChanged, PayloadUnchanged, PayloadChanged }
internal readonly record struct LsbRgba(byte Red, byte Green, byte Blue, byte Alpha);
internal sealed record LsbProbeChannelFact(LsbChannel Channel, LsbProbeSelectionState State, int? FrameBitIndex, int? MessageBit, int BeforeBit, int AfterBit, int Delta);
internal sealed record LsbPixelProbe(int X, int Y, LsbRgba Cover, LsbRgba Stego, bool IsEligible, IReadOnlyList<LsbProbeChannelFact> Channels);

internal readonly record struct LsbBitDistribution(long ZeroCount, long OneCount, double? OneRatio, double? BinaryEntropy);
internal readonly record struct LsbChiSquare(double Value, int DegreesOfFreedom, double? PValue, long SampleCount);
internal readonly record struct LsbAdjacency(long Count00, long Count01, long Count10, long Count11)
{
    public long PairCount => Count00 + Count01 + Count10 + Count11;
    public double? TransitionRate => PairCount == 0 ? null : (Count01 + Count10) / (double)PairCount;
    public double? EqualRate => PairCount == 0 ? null : (Count00 + Count11) / (double)PairCount;
}

/// <summary>一侧图片在明确 Scope 下的统计；p 值不是“图片含隐写的概率”。</summary>
internal sealed record LsbStatistics(
    LsbStatisticsScope Scope,
    long SampleCount,
    LsbBitDistribution Distribution,
    LsbChiSquare PairOfValues,
    LsbAdjacency Horizontal,
    LsbAdjacency Vertical);

internal sealed record LsbChannelStatisticsComparison(LsbStatistics Cover, LsbStatistics Stego);
internal sealed record LsbStatisticsComparison(
    LsbStatistics Cover,
    LsbStatistics Stego,
    IReadOnlyDictionary<LsbChannel, LsbChannelStatisticsComparison> ByChannel);

internal enum LsbFragilityPreset { Jpeg95, Jpeg80, Jpeg60, Scale75, Scale50, GaussianLight, GaussianMedium, Median3 }

internal sealed record LsbBer(long ErrorBits, long ComparedBits, string? UnavailableReason = null)
{
    public double? Ratio => ComparedBits == 0 ? null : ErrorBits / (double)ComparedBits;
}

internal sealed record LsbFragilityResult(
    LsbFragilityPreset Preset,
    PixelImage Image,
    LsbExtractionResult Extraction,
    LsbBer FrameBer,
    LsbBer HeaderBer,
    LsbBer PayloadBer,
    double? PsnrRgbDb);
