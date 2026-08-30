using System.Buffers.Binary;
using System.Text;
using ImageLabPlugin.Domain.Checksums;

namespace ImageLabPlugin.Domain.Steganography;

/// <summary>编码和验证独立的 ILSB V1 Frame，不知道图片或槽位。</summary>
/// <remarks>
/// Header 固定 20 字节，多字节整数显式 little-endian；Frame 写入像素时再按每字节 MSB-first 展开。
/// Header CRC 先于长度字段被信任，避免损坏长度驱动大缓冲分配。
/// </remarks>
internal sealed class LsbFrameCodec
{
    public const int HeaderLength = 20;
    public const byte Version = 1;
    private static ReadOnlySpan<byte> Magic => "ILSB"u8;

    public byte[] Encode(LsbPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var bytes = payload.Bytes.Span;
        var frame = new byte[checked(HeaderLength + bytes.Length)];
        Magic.CopyTo(frame);
        frame[4] = Version;
        frame[5] = (byte)payload.Kind;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(6, 2), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(8, 4), checked((uint)bytes.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(12, 4), Crc32.Compute(bytes));
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16, 4), Crc32.Compute(frame.AsSpan(0, 16)));
        bytes.CopyTo(frame.AsSpan(HeaderLength));
        return frame;
    }

    public (LsbReadStatus Status, LsbFrameHeader? Header, string Explanation) ParseHeader(ReadOnlySpan<byte> headerBytes, long payloadCapacityBytes)
    {
        if (headerBytes.Length < HeaderLength) return (LsbReadStatus.InsufficientSlots, null, "可用槽位不足以读取固定 20 字节 Header。");
        var header = headerBytes[..HeaderLength];
        if (!header[..4].SequenceEqual(Magic)) return (LsbReadStatus.MagicMismatch, null, "未找到独立的 ILSB Magic；不会尝试猜测参数。");
        if (header[4] != Version) return (LsbReadStatus.UnsupportedVersion, null, $"不支持 ILSB 版本 {header[4]}。");
        if (BinaryPrimitives.ReadUInt16LittleEndian(header[6..8]) != 0) return (LsbReadStatus.UnsupportedFlags, null, "ILSB V1 不接受未知 Flags。");
        var kind = (LsbPayloadKind)header[5];
        if (!Enum.IsDefined(kind)) return (LsbReadStatus.UnknownPayloadKind, null, "Header 包含未知 PayloadKind。");
        var expectedHeaderCrc = BinaryPrimitives.ReadUInt32LittleEndian(header[16..20]);
        if (expectedHeaderCrc != Crc32.Compute(header[..16])) return (LsbReadStatus.HeaderCrcMismatch, null, "Header CRC 失败；长度字段未被信任。");
        var length = BinaryPrimitives.ReadUInt32LittleEndian(header[8..12]);
        if (length > LsbPayload.MaximumBytes || length > payloadCapacityBytes)
            return (LsbReadStatus.LengthOutOfRange, null, "Payload 长度超过 V1 上限或当前图片容量。");
        return (LsbReadStatus.Success, new(kind, checked((int)length), BinaryPrimitives.ReadUInt32LittleEndian(header[12..16])), "Header 验证通过。");
    }

    public LsbExtractionResult ValidateComplete(ReadOnlySpan<byte> frame, long payloadCapacityBytes)
    {
        var parsed = ParseHeader(frame, payloadCapacityBytes);
        if (parsed.Status != LsbReadStatus.Success || parsed.Header is null)
            return new(parsed.Status, null, null, frame[..Math.Min(frame.Length, HeaderLength)].ToArray(), parsed.Explanation);
        var required = checked(HeaderLength + parsed.Header.PayloadLength);
        if (frame.Length < required) return new(LsbReadStatus.InsufficientSlots, parsed.Header, null, frame.ToArray(), "Frame 声明的 Payload 没有足够槽位。");
        var exact = frame[..required].ToArray();
        var payload = exact.AsSpan(HeaderLength).ToArray();
        if (Crc32.Compute(payload) != parsed.Header.PayloadCrc32)
            return new(LsbReadStatus.PayloadCrcMismatch, parsed.Header, null, exact, "Payload CRC 失败；CRC 不提供身份认证。");
        if (parsed.Header.PayloadKind == LsbPayloadKind.Utf8Text)
        {
            try { _ = new UTF8Encoding(false, true).GetString(payload); }
            catch (DecoderFallbackException) { return new(LsbReadStatus.InvalidUtf8, parsed.Header, null, exact, "Payload CRC 通过，但内容不是严格 UTF-8。"); }
        }

        return new(LsbReadStatus.Success, parsed.Header, payload, exact, "Frame、Header CRC 与 Payload CRC 均通过；这不表示来源可信。");
    }

    public static bool ReadFrameBit(ReadOnlySpan<byte> frame, int bitIndex)
    {
        if ((uint)bitIndex >= (uint)(frame.Length * 8)) throw new ArgumentOutOfRangeException(nameof(bitIndex));
        return (frame[bitIndex / 8] & (1 << (7 - (bitIndex % 8)))) != 0;
    }
}
