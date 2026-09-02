using System.Buffers.Binary;
using ImageLabPlugin.Domain.Shared.Checksums;
using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Domain.Steganography;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class LsbFrameAndCapacityTests
{
    [Fact]
    public void Crc32符合IEEE标准向量且空输入为零()
    {
        Assert.Equal(0u, Crc32.Compute([]));
        Assert.Equal(0xcbf43926u, Crc32.Compute("123456789"u8));
    }

    [Fact]
    public void 空文本Frame固定为20字节LittleEndianHeader()
    {
        using var payload = LsbPayload.FromText(string.Empty);
        var frame = new LsbFrameCodec().Encode(payload);

        Assert.Equal(20, frame.Length);
        Assert.Equal("ILSB"u8.ToArray(), frame[..4]);
        Assert.Equal(1, frame[4]);
        Assert.Equal((byte)LsbPayloadKind.Utf8Text, frame[5]);
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(6, 2)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(8, 4)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(12, 4)));
        Assert.Equal(Crc32.Compute(frame.AsSpan(0, 16)), BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(16, 4)));
    }

    [Fact]
    public void Frame按Byte内MSBFirst读取且二进制00FF回读()
    {
        using var payload = new LsbPayload(LsbPayloadKind.Binary, [0x00, 0xff]);
        var codec = new LsbFrameCodec();
        var frame = codec.Encode(payload);

        Assert.False(LsbFrameCodec.ReadFrameBit(frame, 0)); // 'I' = 01001001
        Assert.True(LsbFrameCodec.ReadFrameBit(frame, 1));
        var result = codec.ValidateComplete(frame, 2);
        Assert.Equal(LsbReadStatus.Success, result.Status);
        Assert.Equal(new byte[] { 0x00, 0xff }, result.Payload);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(159, 0)]
    [InlineData(160, 0)]
    [InlineData(168, 1)]
    public void 容量边界包含20字节Frame开销(int slots, int expectedPayloadBytes)
    {
        var size = new ImageSize(Math.Max(1, slots), 1);
        var capacity = new LsbCapacityCalculator().Calculate(size, slots, new(LsbChannelStrategy.Red, 0, LsbPlacementKind.Sequential, 0), 0);
        Assert.Equal(expectedPayloadBytes, capacity.PayloadCapacityBytes);
        Assert.Equal(slots >= 160, capacity.Fits);
    }

    [Fact]
    public void 损坏HeaderCrc时不信任伪造的大长度()
    {
        using var payload = LsbPayload.FromText("x");
        var frame = new LsbFrameCodec().Encode(payload);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(8, 4), uint.MaxValue);

        var result = new LsbFrameCodec().ValidateComplete(frame, 65_536);

        Assert.Equal(LsbReadStatus.HeaderCrcMismatch, result.Status);
        Assert.Null(result.Header);
        Assert.Null(result.Payload);
    }

    [Fact]
    public void 严格Utf8拒绝替换字符式恢复()
    {
        using var binary = new LsbPayload(LsbPayloadKind.Binary, [0xff]);
        var codec = new LsbFrameCodec();
        var frame = codec.Encode(binary);
        frame[5] = (byte)LsbPayloadKind.Utf8Text;
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16, 4), Crc32.Compute(frame.AsSpan(0, 16)));

        Assert.Equal(LsbReadStatus.InvalidUtf8, codec.ValidateComplete(frame, 1).Status);
    }

    [Fact]
    public void 最大二进制Payload准确编码长度与LittleEndian字段()
    {
        using var payload = new LsbPayload(LsbPayloadKind.Binary, new byte[LsbPayload.MaximumBytes]);
        var frame = new LsbFrameCodec().Encode(payload);
        Assert.Equal(LsbFrameCodec.HeaderLength + 65_536, frame.Length);
        Assert.Equal(65_536u, BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(8, 4)));
        Assert.Equal(LsbReadStatus.Success, new LsbFrameCodec().ValidateComplete(frame, 65_536).Status);
    }
}
