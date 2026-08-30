using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Domain.Steganography;

/// <summary>执行不原地修改输入的 bit 0/1 replacement。</summary>
internal sealed class LsbEmbeddingEngine(IEnumerable<ILsbSlotOrder> orders)
{
    private readonly IReadOnlyDictionary<LsbPlacementKind, ILsbSlotOrder> _orders = BuildOrders(orders);

    public LsbEmbeddingResult Embed(PixelImage source, LsbSlotLayout layout, ReadOnlySpan<byte> frame, LsbRecipe recipe, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(layout);
        recipe.Validate();
        if (source.Size != layout.Size) throw new ArgumentException("槽位布局不属于当前图片。", nameof(layout));
        var requiredBits = checked(frame.Length * 8);
        var selected = ResolveOrder(recipe).Select(layout.GetEligibleSlotCount(recipe.Channels), requiredBits, recipe.Seed, token);
        var output = source.Clone();
        var target = output.WritableRgba;
        var sourceBytes = source.Rgba.Span;
        var mask = 1 << recipe.BitPlane;
        long changed = 0, negative = 0, positive = 0;
        var byChannel = new long[3];
        var columns = Math.Min(16, source.Size.Width);
        var rows = Math.Min(16, source.Size.Height);
        var selectedGrid = new long[columns * rows];
        var changedGrid = new long[columns * rows];

        // 槽位按 Frame 顺序写；每 16K bit 棃查取消，取消通过异常退出，绝不返回半成品。
        for (var bitIndex = 0; bitIndex < selected.Length; bitIndex++)
        {
            if ((bitIndex & 0x3fff) == 0) token.ThrowIfCancellationRequested();
            var slot = layout.Resolve(selected[bitIndex], recipe.Channels);
            var messageBit = LsbFrameCodec.ReadFrameBit(frame, bitIndex) ? 1 : 0;
            var before = target[slot.RgbaOffset];
            var after = (byte)((before & ~mask) | (messageBit << recipe.BitPlane));
            target[slot.RgbaOffset] = after;
            var x = slot.PixelIndex % source.Size.Width;
            var y = slot.PixelIndex / source.Size.Width;
            var cell = (y * rows / source.Size.Height * columns) + (x * columns / source.Size.Width);
            selectedGrid[cell]++;
            if (after == before) continue;
            changed++; byChannel[(int)slot.Channel]++; changedGrid[cell]++;
            if (after < before) negative++; else positive++;
        }

        double squared = 0;
        for (var index = 0; index < source.Size.PixelCount; index++)
        for (var channel = 0; channel < 3; channel++)
        {
            var offset = checked((index * 4) + channel);
            var delta = target[offset] - sourceBytes[offset];
            squared += delta * delta;
        }
        var mse = squared / (source.Size.PixelCount * 3d);
        double? psnr = mse == 0 ? null : 10d * Math.Log10((255d * 255d) / mse);
        var grid = new List<LsbChangeCell>(columns * rows);
        for (var row = 0; row < rows; row++)
        for (var column = 0; column < columns; column++)
        {
            var index = (row * columns) + column;
            grid.Add(new(column, row, selectedGrid[index], changedGrid[index]));
        }
        var facts = new LsbEmbeddingFacts(requiredBits, LsbFrameCodec.HeaderLength * 8, Math.Max(0, requiredBits - (LsbFrameCodec.HeaderLength * 8)),
            selected, changed, selected.Length - changed, negative, positive,
            new Dictionary<LsbChannel, long> { [LsbChannel.Red] = byChannel[0], [LsbChannel.Green] = byChannel[1], [LsbChannel.Blue] = byChannel[2] }, grid, mse, psnr);
        return new(output, facts);
    }

    internal ILsbSlotOrder ResolveOrder(LsbRecipe recipe) => _orders.TryGetValue(recipe.Placement, out var order)
        ? order : throw new InvalidOperationException($"未登记槽位 Strategy：{recipe.Placement}");

    private static IReadOnlyDictionary<LsbPlacementKind, ILsbSlotOrder> BuildOrders(IEnumerable<ILsbSlotOrder> orders)
    {
        var values = orders.ToArray();
        if (values.GroupBy(x => x.Kind).Any(group => group.Count() != 1)) throw new InvalidOperationException("每种 LSB 槽位 Strategy 必须且只能登记一次。");
        return values.ToDictionary(x => x.Kind);
    }
}

/// <summary>复用同一槽位 Strategy，先安全读取固定 Header，再按受限长度读取 Payload。</summary>
internal sealed class LsbExtractionEngine(LsbFrameCodec codec, IEnumerable<ILsbSlotOrder> orders)
{
    private readonly IReadOnlyDictionary<LsbPlacementKind, ILsbSlotOrder> _orders = orders.ToDictionary(x => x.Kind);

    public LsbExtractionResult Extract(PixelImage image, LsbSlotLayout layout, LsbRecipe recipe, CancellationToken token)
    {
        recipe.Validate();
        var eligible = layout.GetEligibleSlotCount(recipe.Channels);
        var headerBits = LsbFrameCodec.HeaderLength * 8;
        if (eligible < headerBits) return new(LsbReadStatus.InsufficientSlots, null, null, [], "可用槽位不足以读取固定 Header。");
        var order = _orders.TryGetValue(recipe.Placement, out var value) ? value : throw new InvalidOperationException("未登记槽位 Strategy。");
        var headerPositions = order.Select(eligible, headerBits, recipe.Seed, token);
        var headerBytes = ReadBytes(image, layout, recipe, headerPositions, token);
        var parsed = codec.ParseHeader(headerBytes, Math.Max(0, (eligible / 8L) - LsbFrameCodec.HeaderLength));
        if (parsed.Status != LsbReadStatus.Success || parsed.Header is null)
            return new(parsed.Status, null, null, headerBytes, parsed.Explanation);
        var requiredBits = checked((LsbFrameCodec.HeaderLength + parsed.Header.PayloadLength) * 8);
        if (requiredBits > eligible) return new(LsbReadStatus.InsufficientSlots, parsed.Header, null, headerBytes, "声明长度超过当前可用槽位。");
        var positions = order.Select(eligible, requiredBits, recipe.Seed, token);
        var frame = ReadBytes(image, layout, recipe, positions, token);
        return codec.ValidateComplete(frame, Math.Max(0, (eligible / 8L) - LsbFrameCodec.HeaderLength));
    }

    private static byte[] ReadBytes(PixelImage image, LsbSlotLayout layout, LsbRecipe recipe, int[] positions, CancellationToken token)
    {
        var result = new byte[(positions.Length + 7) / 8];
        var bytes = image.Rgba.Span;
        for (var bitIndex = 0; bitIndex < positions.Length; bitIndex++)
        {
            if ((bitIndex & 0x3fff) == 0) token.ThrowIfCancellationRequested();
            var slot = layout.Resolve(positions[bitIndex], recipe.Channels);
            var bit = (bytes[slot.RgbaOffset] >> recipe.BitPlane) & 1;
            result[bitIndex / 8] |= (byte)(bit << (7 - (bitIndex % 8)));
        }
        return result;
    }
}

internal static class LsbBerCalculator
{
    public static (LsbBer Frame, LsbBer Header, LsbBer Payload) Compare(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual)
    {
        var comparable = Math.Min(expected.Length, actual.Length);
        long headerErrors = 0, payloadErrors = 0;
        for (var index = 0; index < comparable; index++)
        {
            var errors = System.Numerics.BitOperations.PopCount((uint)(expected[index] ^ actual[index]));
            if (index < LsbFrameCodec.HeaderLength) headerErrors += errors; else payloadErrors += errors;
        }
        var headerBytes = Math.Min(comparable, LsbFrameCodec.HeaderLength);
        var payloadBytes = Math.Max(0, comparable - LsbFrameCodec.HeaderLength);
        return (new(headerErrors + payloadErrors, comparable * 8L, comparable < expected.Length ? "仅比较攻击后仍可读取的前缀" : null),
            new(headerErrors, headerBytes * 8L), new(payloadErrors, payloadBytes * 8L));
    }
}
