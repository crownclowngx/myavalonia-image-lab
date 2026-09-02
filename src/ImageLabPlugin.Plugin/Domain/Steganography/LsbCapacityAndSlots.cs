using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Domain.Steganography;

/// <summary>在任何图片复制或位置数组分配前完成容量预检。</summary>
internal sealed class LsbCapacityCalculator
{
    public LsbCapacity Calculate(ImageSize size, long opaquePixelCount, LsbRecipe recipe, int payloadLength)
    {
        recipe.Validate();
        if (opaquePixelCount < 0 || opaquePixelCount > size.PixelCount) throw new ArgumentOutOfRangeException(nameof(opaquePixelCount));
        if (payloadLength is < 0 or > LsbPayload.MaximumBytes) throw new ArgumentOutOfRangeException(nameof(payloadLength));
        checked
        {
            var slots = opaquePixelCount * recipe.ChannelCount;
            var frameCapacity = slots / 8;
            var payloadCapacity = Math.Max(0, frameCapacity - LsbFrameCodec.HeaderLength);
            var requiredBits = (LsbFrameCodec.HeaderLength + (long)payloadLength) * 8;
            return new(opaquePixelCount, slots, frameCapacity, payloadCapacity,
                checked((int)Math.Min(payloadCapacity, LsbPayload.MaximumBytes)), requiredBits,
                size.PixelCount == 0 ? 0 : requiredBits / (double)size.PixelCount,
                slots == 0 ? double.PositiveInfinity : requiredBits / (double)slots,
                requiredBits <= slots);
        }
    }
}

/// <summary>冻结 Alpha=255 资格规则和 R→G→B 逻辑槽位映射。</summary>
/// <remarks>
/// 构造时仅保存不透明像素的一维索引，随后所有写入、提取、统计和探针都复用这一个映射事实。
/// 半透明/透明像素完全不进入槽位，其四个 RGBA 字节不会被写入器修改。
/// </remarks>
internal sealed class LsbSlotLayout
{
    private readonly int[] _opaquePixels;

    public LsbSlotLayout(PixelImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var bytes = image.Rgba.Span;
        var pixels = new List<int>();
        for (var pixel = 0; pixel < image.Size.PixelCount; pixel++)
        {
            if (bytes[(pixel * 4) + 3] == byte.MaxValue) pixels.Add(pixel);
        }

        Size = image.Size;
        _opaquePixels = pixels.ToArray();
    }

    public ImageSize Size { get; }
    public int OpaquePixelCount => _opaquePixels.Length;

    public int GetEligibleSlotCount(LsbChannelStrategy strategy) => checked(_opaquePixels.Length * (strategy == LsbChannelStrategy.RgbRoundRobin ? 3 : 1));

    public LsbSlot Resolve(int logicalIndex, LsbChannelStrategy strategy)
    {
        var channelCount = strategy == LsbChannelStrategy.RgbRoundRobin ? 3 : 1;
        var total = checked(_opaquePixels.Length * channelCount);
        if ((uint)logicalIndex >= (uint)total) throw new ArgumentOutOfRangeException(nameof(logicalIndex));
        var opaqueIndex = logicalIndex / channelCount;
        var channel = strategy == LsbChannelStrategy.RgbRoundRobin
            ? (LsbChannel)(logicalIndex % 3)
            : strategy switch { LsbChannelStrategy.Red => LsbChannel.Red, LsbChannelStrategy.Green => LsbChannel.Green, _ => LsbChannel.Blue };
        var pixel = _opaquePixels[opaqueIndex];
        return new(logicalIndex, pixel, channel, checked((pixel * 4) + (int)channel));
    }

    public int? TryGetLogicalIndex(int pixelIndex, LsbChannel channel, LsbChannelStrategy strategy)
    {
        var opaqueIndex = Array.BinarySearch(_opaquePixels, pixelIndex);
        if (opaqueIndex < 0) return null;
        if (strategy != LsbChannelStrategy.RgbRoundRobin)
        {
            var selected = strategy switch { LsbChannelStrategy.Red => LsbChannel.Red, LsbChannelStrategy.Green => LsbChannel.Green, _ => LsbChannel.Blue };
            return selected == channel ? opaqueIndex : null;
        }
        return checked((opaqueIndex * 3) + (int)channel);
    }
}

/// <summary>槽位顺序 Strategy 的最小契约：精确、无重复、无越界且相同输入可复现。</summary>
internal interface ILsbSlotOrder
{
    LsbPlacementKind Kind { get; }
    int[] Select(int eligibleSlots, int requestedCount, ulong seed, CancellationToken cancellationToken);
}

internal sealed class SequentialLsbSlotOrder : ILsbSlotOrder
{
    public LsbPlacementKind Kind => LsbPlacementKind.Sequential;
    public int[] Select(int eligibleSlots, int requestedCount, ulong seed, CancellationToken cancellationToken)
    {
        Validate(eligibleSlots, requestedCount);
        var result = new int[requestedCount];
        for (var index = 0; index < requestedCount; index++)
        {
            if ((index & 0x3fff) == 0) cancellationToken.ThrowIfCancellationRequested();
            result[index] = index;
        }
        return result;
    }

    internal static void Validate(int eligibleSlots, int requestedCount)
    {
        if (eligibleSlots < 0) throw new ArgumentOutOfRangeException(nameof(eligibleSlots));
        if (requestedCount < 0 || requestedCount > eligibleSlots) throw new ArgumentOutOfRangeException(nameof(requestedCount));
    }
}

/// <summary>SplitMix64-v1 与稀疏 partial Fisher-Yates 的确定性无放回选择。</summary>
/// <remarks>
/// seed 只保证复现而不保密。拒绝采样消除取模偏差；交换表仅随 Frame bit 数增长，
/// 不会为最多约 4800 万逻辑槽预先分配完整排列。更换常量或采样规则必须升级 PlacementVersion。
/// </remarks>
internal sealed class PseudoRandomLsbSlotOrder : ILsbSlotOrder
{
    public LsbPlacementKind Kind => LsbPlacementKind.PseudoRandom;

    public int[] Select(int eligibleSlots, int requestedCount, ulong seed, CancellationToken cancellationToken)
    {
        SequentialLsbSlotOrder.Validate(eligibleSlots, requestedCount);
        var random = new SplitMix64(seed);
        var swaps = new Dictionary<int, int>(Math.Min(requestedCount, 1_000_000));
        var result = new int[requestedCount];
        for (var index = 0; index < requestedCount; index++)
        {
            if ((index & 0x3fff) == 0) cancellationToken.ThrowIfCancellationRequested();
            var selected = checked(index + (int)random.NextBounded((uint)(eligibleSlots - index)));
            var valueAtSelected = swaps.GetValueOrDefault(selected, selected);
            var valueAtIndex = swaps.GetValueOrDefault(index, index);
            swaps[selected] = valueAtIndex;
            swaps.Remove(index);
            result[index] = valueAtSelected;
        }
        return result;
    }
}

/// <summary>版本化 SplitMix64；公开常量使 Golden Vector 不依赖运行时 Random 实现。</summary>
internal struct SplitMix64(ulong state)
{
    private ulong _state = state;

    public ulong Next()
    {
        var value = _state += 0x9e3779b97f4a7c15UL;
        value = (value ^ (value >> 30)) * 0xbf58476d1ce4e5b9UL;
        value = (value ^ (value >> 27)) * 0x94d049bb133111ebUL;
        return value ^ (value >> 31);
    }

    public uint NextBounded(uint bound)
    {
        if (bound == 0) throw new ArgumentOutOfRangeException(nameof(bound));
        var threshold = unchecked((0UL - bound) % bound);
        while (true)
        {
            var value = Next();
            if (value >= threshold) return checked((uint)(value % bound));
        }
    }
}
