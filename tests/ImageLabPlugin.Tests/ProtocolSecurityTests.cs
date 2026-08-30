using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.Watermarking;
using ImageLabPlugin.Infrastructure.Cryptography;
using ImageLabPlugin.Infrastructure.ErrorCorrection;
using ImageLabPlugin.Infrastructure.Watermarking;
using ImageLabPlugin.Domain.Checksums;
using Xunit;

namespace ImageLabPlugin.Tests;

/// <summary>冻结 V1 线格式并覆盖安全边界、篡改拒绝和随机性。</summary>
public sealed class ProtocolSecurityTests
{
    [Fact]
    public void 固定输入的Frame摘要作为跨进程GoldenVector()
    {
        var codec = new ReedSolomonCodec();
        var protocol = new WatermarkFrameProtocol(codec, new CountingRandomSource());
        using var payload = new WatermarkPayload(Encoding.UTF8.GetBytes("golden-vector-v1"), PayloadContentType.Text);

        var frame = protocol.Encode(payload, EmbeddingProfileId.Balanced, password: null);

        var headerHash = Convert.ToHexString(SHA256.HashData(frame.EncodedHeader));
        var dataHash = Convert.ToHexString(SHA256.HashData(frame.EncodedData));
        var mappingHash = Convert.ToHexString(SHA256.HashData(frame.MappingKey));
        Assert.Equal("D388DF37888F1C2CB1478147E1FD52CA", headerHash[..32]);
        Assert.Equal("DBF891A3F5A0BE2900A977B84208CE11", headerHash[32..]);
        Assert.Equal("65AF15A952F66721276D79BD9A913C81", dataHash[..32]);
        Assert.Equal("D65CD87AFAD97FD79297AD8AD64B032E", dataHash[32..]);
        Assert.Equal("D9CA6FDDADA7AE75E4C107B6B8B6CF5F", mappingHash[..32]);
        Assert.Equal("BAF9CF69A1CA9EF74C704525BE3A93F8", mappingHash[32..]);
    }

    [Fact]
    public void 可压缩内容会先压缩并能按原始长度恢复()
    {
        var protocol = CreateProtocol(new CountingRandomSource());
        var bytes = Enumerable.Repeat((byte)0x41, 4096).ToArray();
        using var payload = new WatermarkPayload(bytes, PayloadContentType.Binary);

        var frame = protocol.Encode(payload, EmbeddingProfileId.Stealth, password: null);
        var header = protocol.DecodeHeader(frame.EncodedHeader, out _);
        using var decoded = protocol.DecodeData(header, frame.EncodedData, password: null).Payload;

        Assert.True(header.Flags.HasFlag(FrameFlags.Compressed));
        Assert.True(header.ProtectedLength < header.OriginalLength);
        Assert.Equal(bytes, decoded.Bytes.ToArray());
    }

    [Fact]
    public void 超过纠错能力的数据篡改必须被拒绝()
    {
        var protocol = CreateProtocol(new CountingRandomSource());
        using var payload = new WatermarkPayload(Enumerable.Range(0, 200).Select(i => (byte)i).ToArray(), PayloadContentType.Binary);
        var frame = protocol.Encode(payload, EmbeddingProfileId.Balanced, password: null);
        var damaged = frame.EncodedData.ToArray();
        for (var i = 0; i < 17; i++)
        {
            damaged[i * 3] ^= (byte)(0x51 + i);
        }

        Assert.ThrowsAny<InvalidDataException>(() => protocol.DecodeData(frame.Header, damaged, password: null));
    }

    [Fact]
    public void 协议拒绝未知Flag并且不能只靠重算Crc绕过()
    {
        var codec = new ReedSolomonCodec();
        var protocol = new WatermarkFrameProtocol(codec, new CountingRandomSource());
        using var payload = new WatermarkPayload(new byte[] { 1, 2, 3 }, PayloadContentType.Binary);
        var frame = protocol.Encode(payload, EmbeddingProfileId.Stealth, password: null);
        var rawHeader = codec.Decode(frame.EncodedHeader, WatermarkFrameProtocol.HeaderLength).Data;
        rawHeader[7] = 0x80;
        BinaryPrimitives.WriteUInt32LittleEndian(rawHeader.AsSpan(76, 4), Crc32.Compute(rawHeader.AsSpan(0, 76)));
        var forgedHeader = codec.Encode(rawHeader);

        Assert.Throws<NotSupportedException>(() => protocol.DecodeHeader(forgedHeader, out _));
    }

    [Fact]
    public void 加密Header不允许无密码解析映射并限制超长密码()
    {
        var protocol = CreateProtocol(new CountingRandomSource());
        using var payload = new WatermarkPayload(new byte[] { 1, 2, 3 }, PayloadContentType.Binary);
        var frame = protocol.Encode(payload, EmbeddingProfileId.Robust, "secret");

        Assert.Throws<UnauthorizedAccessException>(() => protocol.ResolveMappingKey(frame.Header, password: null));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            protocol.Encode(payload, EmbeddingProfileId.Robust, new string('x', 1025)));
    }

    [Fact]
    public void 生产随机源使相同明文生成不同的SaltNonce和映射种子()
    {
        var protocol = CreateProtocol(new CryptographicRandomSource());
        using var firstPayload = new WatermarkPayload(new byte[] { 4, 5, 6 }, PayloadContentType.Binary);
        using var secondPayload = new WatermarkPayload(new byte[] { 4, 5, 6 }, PayloadContentType.Binary);

        var first = protocol.Encode(firstPayload, EmbeddingProfileId.Balanced, "password");
        var second = protocol.Encode(secondPayload, EmbeddingProfileId.Balanced, "password");

        Assert.NotEqual(first.Header.Salt, second.Header.Salt);
        Assert.NotEqual(first.Header.Nonce, second.Header.Nonce);
        Assert.NotEqual(first.Header.MappingSeed, second.Header.MappingSeed);
        Assert.NotEqual(first.EncodedData, second.EncodedData);
    }

    [Fact]
    public void Payload绝对长度上限在分配协议缓冲区前生效()
    {
        var oversized = new byte[WatermarkPayload.MaximumPayloadBytes + 1];

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WatermarkPayload(oversized, PayloadContentType.Binary));
    }

    private static WatermarkFrameProtocol CreateProtocol(IRandomSource randomSource) =>
        new(new ReedSolomonCodec(), randomSource);

    private sealed class CountingRandomSource : IRandomSource
    {
        private byte _next;

        public void Fill(Span<byte> destination)
        {
            for (var i = 0; i < destination.Length; i++)
            {
                destination[i] = _next++;
            }
        }
    }
}
