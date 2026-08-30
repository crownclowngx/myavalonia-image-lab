using ImageLabPlugin.Domain.Frequency;
using ImageLabPlugin.Domain.Imaging;
using ImageLabPlugin.Domain.Watermarking;
using ImageLabPlugin.Infrastructure.ErrorCorrection;

namespace ImageLabPlugin.Infrastructure.Watermarking;

internal sealed record HeaderReadResult(WatermarkFrameHeader Header, int CorrectedSymbols, double Confidence);
internal sealed record DataReadResult(byte[] EncodedData, double Confidence);

/// <summary>在 Y 通道的 8×8 DCT 中频系数上承载 V1 Frame。</summary>
/// <remarks>
/// Control Channel 使用固定强度和三副本，读取 Header 后再按 Profile 和 Mapping Key 定位 Data Channel。
/// Carrier 只负责 bit 与系数之间的映射，不解释压缩、密码或 Payload 内容。
/// </remarks>
internal sealed class FrequencyWatermarkCarrier(
    Dct8x8Transform transform,
    WatermarkFrameProtocol frameProtocol,
    ReedSolomonCodec errorCorrection)
{
    public const int HeaderRedundancy = 3;
    public const double HeaderQimStep = 40d;
    public const int ControlSlotCount = WatermarkFrameProtocol.EncodedHeaderLength * 8 * HeaderRedundancy;

    private static readonly (int U, int V)[] Coefficients =
    [
        (2, 2),
        (3, 1),
        (1, 3),
        (3, 2)
    ];

    public CapacityEstimate Estimate(
        PixelImage image,
        EmbeddingProfileId profileId,
        int payloadLength,
        bool encrypted)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (payloadLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(payloadLength));
        }

        var slots = CreateSlots(image);
        var dataSlots = Math.Max(0, slots.Count - ControlSlotCount);
        var profile = EmbeddingProfile.Resolve(profileId);
        var physicalBytes = dataSlots / profile.DataRedundancy / 8;
        var maximumProtected = FindMaximumProtectedLength(physicalBytes);
        var encryptionOverhead = encrypted ? 16 : 0;
        var maximumPayload = Math.Max(0, maximumProtected - encryptionOverhead);
        return new CapacityEstimate(
            slots.Count,
            Math.Min(slots.Count, ControlSlotCount),
            dataSlots,
            maximumProtected,
            maximumPayload,
            checked(payloadLength + encryptionOverhead));
    }

    public PixelImage Embed(PixelImage source, EncodedWatermarkFrame frame, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(frame);
        var slots = CreateSlots(source);
        if (slots.Count < ControlSlotCount)
        {
            throw new InvalidOperationException("图片尺寸或可用不透明区域不足以承载 V1 Control Channel。");
        }

        var controlIndices = CreateControlIndices(slots.Count);
        var reserved = new bool[slots.Count];
        foreach (var index in controlIndices)
        {
            reserved[index] = true;
        }

        var profile = EmbeddingProfile.Resolve(frame.Header.Profile);
        var dataIndices = CreateDataIndices(reserved, frame.MappingKey);
        var requiredDataSlots = checked(frame.EncodedData.Length * 8 * profile.DataRedundancy);
        if (requiredDataSlots > dataIndices.Length)
        {
            throw new InvalidOperationException("编码后的 Payload 超过当前图片和 Profile 的实际容量。");
        }

        var assignments = new Dictionary<int, List<CoefficientAssignment>>();
        for (var copy = 0; copy < HeaderRedundancy; copy++)
        {
            for (var bit = 0; bit < frame.EncodedHeader.Length * 8; bit++)
            {
                var slot = slots[controlIndices[(copy * frame.EncodedHeader.Length * 8) + bit]];
                AddAssignment(assignments, slot, ReadBit(frame.EncodedHeader, bit), HeaderQimStep);
            }
        }

        var dataCursor = 0;
        for (var bit = 0; bit < frame.EncodedData.Length * 8; bit++)
        {
            var value = ReadBit(frame.EncodedData, bit);
            for (var copy = 0; copy < profile.DataRedundancy; copy++)
            {
                var slot = slots[dataIndices[dataCursor++]];
                AddAssignment(assignments, slot, value, profile.DataQimStep);
            }
        }

        var luma = ColorSpaceConverter.ExtractLuma(source);
        Span<double> spatial = stackalloc double[64];
        Span<double> frequency = stackalloc double[64];
        foreach (var pair in assignments.OrderBy(item => item.Key))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var blockX = pair.Key % (source.Size.Width / 8);
            var blockY = pair.Key / (source.Size.Width / 8);
            ReadBlock(luma, blockX, blockY, spatial);
            transform.Forward(spatial, frequency);
            foreach (var assignment in pair.Value)
            {
                var coefficientIndex = (assignment.V * 8) + assignment.U;
                frequency[coefficientIndex] = QimModulator.Embed(
                    frequency[coefficientIndex],
                    assignment.Bit,
                    assignment.Step);
            }

            transform.Inverse(frequency, spatial);
            WriteBlock(luma, blockX, blockY, spatial);
        }

        return ColorSpaceConverter.ApplyLuma(source, luma);
    }

    public HeaderReadResult ReadHeader(PixelImage image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        var slots = CreateSlots(image);
        if (slots.Count < ControlSlotCount)
        {
            throw new InvalidDataException("图片没有足够的载体槽位，无法包含 V1 Control Channel。");
        }

        var indices = CreateControlIndices(slots.Count);
        var encodedHeader = new byte[WatermarkFrameProtocol.EncodedHeaderLength];
        var cache = new Dictionary<int, double[]>();
        var luma = ColorSpaceConverter.ExtractLuma(image);
        double confidenceSum = 0;
        var bitCount = encodedHeader.Length * 8;
        for (var bit = 0; bit < bitCount; bit++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double score = 0;
            double weight = 0;
            for (var copy = 0; copy < HeaderRedundancy; copy++)
            {
                var slot = slots[indices[(copy * bitCount) + bit]];
                var decision = ReadSlot(image, luma, slot, HeaderQimStep, cache);
                var decisionWeight = 0.25d + (0.75d * decision.Confidence);
                score += decision.Bit ? decisionWeight : -decisionWeight;
                weight += decisionWeight;
                confidenceSum += decision.Confidence;
            }

            WriteBit(encodedHeader, bit, score >= 0);
            _ = weight;
        }

        var header = frameProtocol.DecodeHeader(encodedHeader, out var corrected);
        return new HeaderReadResult(header, corrected, confidenceSum / (bitCount * HeaderRedundancy));
    }

    public DataReadResult ReadData(
        PixelImage image,
        WatermarkFrameHeader header,
        ReadOnlySpan<byte> mappingKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        var slots = CreateSlots(image);
        var controlIndices = CreateControlIndices(slots.Count);
        var reserved = new bool[slots.Count];
        foreach (var index in controlIndices)
        {
            reserved[index] = true;
        }

        var profile = EmbeddingProfile.Resolve(header.Profile);
        var indices = CreateDataIndices(reserved, mappingKey);
        var requiredSlots = checked(header.EncodedLength * 8 * profile.DataRedundancy);
        if (requiredSlots > indices.Length)
        {
            throw new InvalidDataException("Header 声明的数据长度超过图片实际载体容量。");
        }

        var encodedData = new byte[header.EncodedLength];
        var cache = new Dictionary<int, double[]>();
        var luma = ColorSpaceConverter.ExtractLuma(image);
        var cursor = 0;
        double confidenceSum = 0;
        for (var bit = 0; bit < encodedData.Length * 8; bit++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double score = 0;
            for (var copy = 0; copy < profile.DataRedundancy; copy++)
            {
                var decision = ReadSlot(image, luma, slots[indices[cursor++]], profile.DataQimStep, cache);
                var weight = 0.25d + (0.75d * decision.Confidence);
                score += decision.Bit ? weight : -weight;
                confidenceSum += decision.Confidence;
            }

            WriteBit(encodedData, bit, score >= 0);
        }

        return new DataReadResult(
            encodedData,
            confidenceSum / Math.Max(1, encodedData.Length * 8 * profile.DataRedundancy));
    }

    private int FindMaximumProtectedLength(int physicalBytes)
    {
        var low = 0;
        var high = Math.Min(physicalBytes, WatermarkPayload.MaximumPayloadBytes + 16);
        while (low < high)
        {
            var candidate = low + ((high - low + 1) / 2);
            if (errorCorrection.GetEncodedLength(candidate) <= physicalBytes)
            {
                low = candidate;
            }
            else
            {
                high = candidate - 1;
            }
        }

        return low;
    }

    private static List<CarrierSlot> CreateSlots(PixelImage image)
    {
        var blockColumns = image.Size.Width / 8;
        var blockRows = image.Size.Height / 8;
        var slots = new List<CarrierSlot>(checked(blockColumns * blockRows * Coefficients.Length));
        for (var blockY = 0; blockY < blockRows; blockY++)
        {
            for (var blockX = 0; blockX < blockColumns; blockX++)
            {
                if (!IsOpaqueBlock(image, blockX, blockY))
                {
                    continue;
                }

                var blockIndex = (blockY * blockColumns) + blockX;
                foreach (var (u, v) in Coefficients)
                {
                    slots.Add(new CarrierSlot(blockIndex, u, v));
                }
            }
        }

        return slots;
    }

    private static bool IsOpaqueBlock(PixelImage image, int blockX, int blockY)
    {
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                if (image.GetAlpha((blockX * 8) + x, (blockY * 8) + y) < 250)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static int[] CreateControlIndices(int slotCount)
    {
        if (slotCount < ControlSlotCount)
        {
            throw new InvalidDataException("载体槽位不足。");
        }

        var result = new int[ControlSlotCount];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = (int)(((long)i * slotCount) / ControlSlotCount);
        }

        return result;
    }

    private static int[] CreateDataIndices(ReadOnlySpan<bool> reserved, ReadOnlySpan<byte> mappingKey)
    {
        var result = new int[reserved.Length - ControlSlotCount];
        var cursor = 0;
        for (var i = 0; i < reserved.Length; i++)
        {
            if (!reserved[i])
            {
                result[cursor++] = i;
            }
        }

        DeterministicPermutation.Shuffle(result, mappingKey);
        return result;
    }

    private QimDecision ReadSlot(
        PixelImage image,
        LumaPlane luma,
        CarrierSlot slot,
        double step,
        Dictionary<int, double[]> cache)
    {
        if (!cache.TryGetValue(slot.BlockIndex, out var frequency))
        {
            var blockColumns = image.Size.Width / 8;
            var blockX = slot.BlockIndex % blockColumns;
            var blockY = slot.BlockIndex / blockColumns;
            var spatial = new double[64];
            frequency = new double[64];
            ReadBlock(luma, blockX, blockY, spatial);
            transform.Forward(spatial, frequency);
            cache.Add(slot.BlockIndex, frequency);
        }

        return QimModulator.Read(frequency[(slot.V * 8) + slot.U], step);
    }

    private static void AddAssignment(
        Dictionary<int, List<CoefficientAssignment>> assignments,
        CarrierSlot slot,
        bool bit,
        double step)
    {
        if (!assignments.TryGetValue(slot.BlockIndex, out var blockAssignments))
        {
            blockAssignments = [];
            assignments.Add(slot.BlockIndex, blockAssignments);
        }

        blockAssignments.Add(new CoefficientAssignment(slot.U, slot.V, bit, step));
    }

    private static void ReadBlock(LumaPlane luma, int blockX, int blockY, Span<double> destination)
    {
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                destination[(y * 8) + x] = luma[(blockX * 8) + x, (blockY * 8) + y];
            }
        }
    }

    private static void WriteBlock(LumaPlane luma, int blockX, int blockY, ReadOnlySpan<double> source)
    {
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                luma[(blockX * 8) + x, (blockY * 8) + y] = source[(y * 8) + x];
            }
        }
    }

    private static bool ReadBit(ReadOnlySpan<byte> bytes, int bitIndex) =>
        (bytes[bitIndex / 8] & (1 << (7 - (bitIndex % 8)))) != 0;

    private static void WriteBit(Span<byte> bytes, int bitIndex, bool value)
    {
        if (value)
        {
            bytes[bitIndex / 8] |= (byte)(1 << (7 - (bitIndex % 8)));
        }
    }

    private readonly record struct CarrierSlot(int BlockIndex, int U, int V);
    private readonly record struct CoefficientAssignment(int U, int V, bool Bit, double Step);
}
