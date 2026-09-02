using System.Buffers.Binary;
using ImageLabPlugin.Domain.Shared.Checksums;
using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Domain.Wavelets;

internal sealed record DwtWatermarkCapacity(int PairSlots, int MaximumPayloadBytes, int RequiredPayloadBytes)
{
    public bool Fits => RequiredPayloadBytes <= MaximumPayloadBytes;
}

internal sealed record DwtWatermarkEmbedResult(PixelImage Image, DwtWatermarkCapacity Capacity, double Step, int Seed);
internal sealed record DwtWatermarkReadResult(bool Detected, bool IntegrityValid, byte[] Payload, double Confidence, string Summary);

/// <summary>实验性 Haar-DWT 系数对差分 QIM 载体，协议 ID 固定为 <c>dwt-pair-qim-v1</c>。</summary>
/// <remarks>
/// 载体只处理版本化短 Frame 与系数对，不复用 DCT 8×8 槽位内部实现。每个 bit 修改一对系数之差，
/// 校正量平均分配给两端，以减少对子带局部平均值的偏移。V1 使用确定性种子排列，供同条件 benchmark
/// 复现；CRC 只验证意外损坏，不提供密码学认证。
/// </remarks>
internal sealed class DwtWatermarkCarrier(
    HaarWaveletTransform transform,
    ImageChannelConverter channelConverter)
{
    public const string CarrierId = "dwt-pair-qim-v1";
    private const int HeaderBytes = 12;
    private static ReadOnlySpan<byte> Magic => "DWT1"u8;

    public DwtWatermarkCapacity Estimate(PixelImage image, int levels, int payloadLength)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (payloadLength < 0) throw new ArgumentOutOfRangeException(nameof(payloadLength));
        var plane = channelConverter.Extract(image, ImageChannel.Luma);
        var pyramid = transform.Forward(plane, levels);
        var slots = CreateSlots(pyramid, seed: 0);
        return new(slots.Length, Math.Max(0, (slots.Length / 8) - HeaderBytes), payloadLength);
    }

    public DwtWatermarkEmbedResult Embed(
        PixelImage source,
        ReadOnlySpan<byte> payload,
        int levels,
        double step,
        int seed,
        CancellationToken cancellationToken = default)
    {
        ValidateStep(step);
        var plane = channelConverter.Extract(source, ImageChannel.Luma, cancellationToken);
        var pyramid = transform.Forward(plane, levels, cancellationToken);
        var slots = CreateSlots(pyramid, seed);
        var capacity = new DwtWatermarkCapacity(slots.Length, Math.Max(0, slots.Length / 8 - HeaderBytes), payload.Length);
        if (!capacity.Fits) throw new InvalidOperationException("Payload 超过当前 DWT 系数对容量。");
        var frame = BuildFrame(payload);

        var coefficients = pyramid.CloneCoefficients();
        for (var bitIndex = 0; bitIndex < frame.Length * 8; bitIndex++)
        {
            if ((bitIndex & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            var slot = slots[bitIndex];
            var bit = (frame[bitIndex / 8] & (1 << (7 - bitIndex % 8))) != 0;
            var difference = coefficients[slot.First] - coefficients[slot.Second];
            var target = QuantizeDifference(difference, bit, step);
            var correction = target - difference;
            coefficients[slot.First] += correction * 0.5d;
            coefficients[slot.Second] -= correction * 0.5d;
        }
        var modified = new WaveletPyramid(pyramid.Transform, pyramid.Channel, pyramid.OriginalSize,
            pyramid.PaddedSize, coefficients, pyramid.Levels);
        var restored = transform.Inverse(modified, cancellationToken);
        var image = channelConverter.Apply(source, restored, MidpointRounding.AwayFromZero).Image;
        return new(image, capacity, step, seed);
    }

    public DwtWatermarkReadResult Read(
        PixelImage image,
        int levels,
        double step,
        int seed,
        CancellationToken cancellationToken = default)
    {
        ValidateStep(step);
        var plane = channelConverter.Extract(image, ImageChannel.Luma, cancellationToken);
        var pyramid = transform.Forward(plane, levels, cancellationToken);
        var slots = CreateSlots(pyramid, seed);
        if (slots.Length < HeaderBytes * 8) return new(false, false, [], 0d, "载体容量不足以包含 DWT V1 Header。");
        var header = ReadBytes(pyramid.Coefficients.Span, slots, HeaderBytes, step, cancellationToken, out var headerConfidence);
        if (!header.AsSpan(0, 4).SequenceEqual(Magic)) return new(false, false, [], headerConfidence, "未检测到 DWT V1 Magic。");
        var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4, 4));
        if (length < 0 || length > (slots.Length / 8) - HeaderBytes)
            return new(true, false, [], headerConfidence, "Header 声明的 Payload 长度超过实际容量。");
        var frame = ReadBytes(pyramid.Coefficients.Span, slots, HeaderBytes + length, step, cancellationToken, out var confidence);
        var payload = frame.AsSpan(HeaderBytes).ToArray();
        var expected = BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(8, 4));
        var valid = expected == Crc32.Compute(payload);
        return new(true, valid, payload, confidence, valid ? "Payload CRC 完整。" : "Payload CRC 不匹配。" );
    }

    /// <summary>对已知共同 Payload 直接比较每个差分 QIM bit，得到纠错前原始 BER。</summary>
    public double MeasureRawBitErrorRate(PixelImage image, ReadOnlySpan<byte> payload, int levels, double step, int seed,
        CancellationToken cancellationToken = default)
    {
        ValidateStep(step);
        var pyramid = transform.Forward(channelConverter.Extract(image, ImageChannel.Luma, cancellationToken), levels, cancellationToken);
        var slots = CreateSlots(pyramid, seed); var frame = BuildFrame(payload);
        if (frame.Length * 8 > slots.Length) throw new InvalidDataException("已知 DWT Frame 超过受测图片实际槽位。");
        var coefficients = pyramid.Coefficients.Span; long errors = 0;
        for (var bitIndex = 0; bitIndex < frame.Length * 8; bitIndex++)
        {
            if ((bitIndex & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            var slot = slots[bitIndex]; var difference = coefficients[slot.First] - coefficients[slot.Second];
            var actual = DistanceToLattice(difference, step, step) < DistanceToLattice(difference, 0d, step);
            var expected = (frame[bitIndex / 8] & (1 << (7 - bitIndex % 8))) != 0;
            if (actual != expected) errors++;
        }
        return errors / (double)(frame.Length * 8);
    }

    private static byte[] ReadBytes(ReadOnlySpan<double> coefficients, ReadOnlySpan<CoefficientPair> slots,
        int byteCount, double step, CancellationToken token, out double confidence)
    {
        var result = new byte[byteCount];
        double confidenceSum = 0d;
        for (var bitIndex = 0; bitIndex < byteCount * 8; bitIndex++)
        {
            if ((bitIndex & 255) == 0) token.ThrowIfCancellationRequested();
            var slot = slots[bitIndex];
            var difference = coefficients[slot.First] - coefficients[slot.Second];
            var zeroDistance = DistanceToLattice(difference, 0d, step);
            var oneDistance = DistanceToLattice(difference, step, step);
            if (oneDistance < zeroDistance) result[bitIndex / 8] |= (byte)(1 << (7 - bitIndex % 8));
            confidenceSum += Math.Clamp(Math.Abs(zeroDistance - oneDistance) / step, 0d, 1d);
        }
        confidence = byteCount == 0 ? 0d : confidenceSum / (byteCount * 8d);
        return result;
    }

    private static byte[] BuildFrame(ReadOnlySpan<byte> payload)
    {
        var frame = new byte[checked(HeaderBytes + payload.Length)];
        Magic.CopyTo(frame); BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4, 4), payload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(8, 4), Crc32.Compute(payload));
        payload.CopyTo(frame.AsSpan(HeaderBytes));
        return frame;
    }

    private static CoefficientPair[] CreateSlots(WaveletPyramid pyramid, int seed)
    {
        var indices = new List<int>();
        foreach (var level in pyramid.Levels)
        foreach (var subband in new[] { WaveletSubband.HorizontalDetail, WaveletSubband.VerticalDetail })
        {
            var region = level.GetRegion(subband);
            for (var y = region.Y; y < region.Bottom; y++)
                for (var x = region.X; x < region.Right; x++)
                    indices.Add((y * pyramid.PaddedSize.Width) + x);
        }
        var random = new DeterministicInt32(seed);
        for (var i = indices.Count - 1; i > 0; i--)
        {
            var target = random.Next(i + 1);
            (indices[i], indices[target]) = (indices[target], indices[i]);
        }
        var pairs = new CoefficientPair[indices.Count / 2];
        for (var i = 0; i < pairs.Length; i++) pairs[i] = new(indices[i * 2], indices[(i * 2) + 1]);
        return pairs;
    }

    private static double QuantizeDifference(double value, bool bit, double step)
    {
        var period = 2d * step;
        var offset = bit ? step : 0d;
        return Math.Round((value - offset) / period, MidpointRounding.AwayFromZero) * period + offset;
    }

    private static double DistanceToLattice(double value, double offset, double step) =>
        Math.Abs(value - (Math.Round((value - offset) / (2d * step), MidpointRounding.AwayFromZero) * 2d * step + offset));

    private static void ValidateStep(double step)
    {
        if (!double.IsFinite(step) || step is < 2d or > 128d)
            throw new ArgumentOutOfRangeException(nameof(step), "DWT 差分 QIM 步长必须是 2–128 的有限数。");
    }

    private readonly record struct CoefficientPair(int First, int Second);

    private sealed class DeterministicInt32(int seed)
    {
        private uint _state = unchecked((uint)seed) + 0x9e3779b9u;
        public int Next(int exclusiveMaximum)
        {
            _state ^= _state << 13; _state ^= _state >> 17; _state ^= _state << 5;
            return (int)(_state % (uint)exclusiveMaximum);
        }
    }
}
