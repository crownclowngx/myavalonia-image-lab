using System.Security.Cryptography;
using System.Text;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.Shared.Spectral;
using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Domain.Watermarking;
using ImageLabPlugin.Infrastructure.ErrorCorrection;
using ImageLabPlugin.Infrastructure.Watermarking;
using Xunit;

namespace ImageLabPlugin.Tests;

/// <summary>
/// 覆盖从二进制协议到频域载体的核心闭环。
/// 这些测试不依赖窗口或文件系统，因此一旦失败，可以直接定位为算法或协议回归。
/// </summary>
public sealed class WatermarkPipelineTests
{
    [Theory]
    [InlineData((int)EmbeddingProfileId.Stealth)]
    [InlineData((int)EmbeddingProfileId.Balanced)]
    [InlineData((int)EmbeddingProfileId.Robust)]
    public void 三种配置均可在像素量化后完整恢复文本(int profileValue)
    {
        var profile = (EmbeddingProfileId)profileValue;
        var services = CreateServices();
        var source = CreateTexturedImage(512, 512);
        var payload = new WatermarkPayload(Encoding.UTF8.GetBytes("ImageLab 频域水印闭环测试"), PayloadContentType.Text);

        var encodedFrame = services.Protocol.Encode(payload, profile, password: null);
        var embedded = services.Carrier.Embed(source, encodedFrame, CancellationToken.None);
        var headerResult = services.Carrier.ReadHeader(embedded, CancellationToken.None);
        var mappingKey = services.Protocol.ResolveMappingKey(headerResult.Header, password: null);
        var dataResult = services.Carrier.ReadData(embedded, headerResult.Header, mappingKey, CancellationToken.None);
        var decoded = services.Protocol.DecodeData(headerResult.Header, dataResult.EncodedData, password: null);

        Assert.Equal(payload.Bytes.ToArray(), decoded.Payload.Bytes.ToArray());
        Assert.Equal(PayloadContentType.Text, decoded.Payload.ContentType);
        Assert.Equal(profile, headerResult.Header.Profile);
        Assert.Equal(IntegrityStatus.Valid, decoded.Integrity);
        Assert.InRange(headerResult.Confidence, 0.20d, 1d);
        Assert.InRange(dataResult.Confidence, 0.20d, 1d);
    }

    [Fact]
    public void 加密协议使用正确密码恢复并用错误密码失败()
    {
        var services = CreateServices();
        var payloadBytes = Encoding.UTF8.GetBytes("这是需要认证加密的水印内容。");
        var payload = new WatermarkPayload(payloadBytes, PayloadContentType.Text);
        var encodedFrame = services.Protocol.Encode(payload, EmbeddingProfileId.Balanced, "correct-password");
        var header = services.Protocol.DecodeHeader(encodedFrame.EncodedHeader, out _);

        var decoded = services.Protocol.DecodeData(header, encodedFrame.EncodedData, "correct-password");

        Assert.Equal(payloadBytes, decoded.Payload.Bytes.ToArray());
        Assert.ThrowsAny<CryptographicException>(() =>
            services.Protocol.DecodeData(header, encodedFrame.EncodedData, "wrong-password"));
    }

    [Fact]
    public void 容量估算包含控制信道纠错和加密开销()
    {
        var services = CreateServices();
        var source = CreateTexturedImage(256, 256);

        var plain = services.Carrier.Estimate(source, EmbeddingProfileId.Balanced, 10, encrypted: false);
        var encrypted = services.Carrier.Estimate(source, EmbeddingProfileId.Balanced, 10, encrypted: true);

        Assert.Equal(FrequencyWatermarkCarrier.ControlSlotCount, plain.ControlSlots);
        Assert.True(plain.MaximumPayloadBytes > encrypted.MaximumPayloadBytes);
        Assert.Equal(16, encrypted.RequiredPayloadBytes - plain.RequiredPayloadBytes);
    }

    [Fact]
    public void 透明块不会被计入容量也不会被改写()
    {
        var services = CreateServices();
        var sourceBytes = CreateTexturedRgba(256, 256);
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                sourceBytes[((y * 256) + x) * 4 + 3] = 0;
            }
        }

        var source = new PixelImage(new ImageSize(256, 256), sourceBytes);

        var estimate = services.Carrier.Estimate(source, EmbeddingProfileId.Stealth, 0, encrypted: false);

        Assert.Equal(((256 / 8) * (256 / 8) * 4) - 4, estimate.CarrierSlots);

        using var payload = new WatermarkPayload(Encoding.UTF8.GetBytes("alpha"), PayloadContentType.Text);
        var frame = services.Protocol.Encode(payload, EmbeddingProfileId.Stealth, password: null);
        var embedded = services.Carrier.Embed(source, frame, CancellationToken.None);
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                Assert.Equal(source.GetPixel(x, y), embedded.GetPixel(x, y));
            }
        }
    }

    [Fact]
    public void 非八倍数边缘和全部Alpha在写入后逐字节不变()
    {
        var services = CreateServices();
        var source = CreateTexturedImage(517, 515);
        using var payload = new WatermarkPayload(Encoding.UTF8.GetBytes("edge"), PayloadContentType.Text);
        var frame = services.Protocol.Encode(payload, EmbeddingProfileId.Balanced, password: null);

        var embedded = services.Carrier.Embed(source, frame, CancellationToken.None);

        for (var y = 0; y < source.Size.Height; y++)
        {
            for (var x = 0; x < source.Size.Width; x++)
            {
                Assert.Equal(source.GetAlpha(x, y), embedded.GetAlpha(x, y));
                if (x >= 512 || y >= 512)
                {
                    Assert.Equal(source.GetPixel(x, y), embedded.GetPixel(x, y));
                }
            }
        }
    }

    [Fact]
    public void 鲁棒配置可恢复每通道正负一的确定性像素噪声()
    {
        var services = CreateServices();
        var source = CreateTexturedImage(768, 768);
        using var payload = new WatermarkPayload(Encoding.UTF8.GetBytes("small-noise"), PayloadContentType.Text);
        var frame = services.Protocol.Encode(payload, EmbeddingProfileId.Robust, password: null);
        var embedded = services.Carrier.Embed(source, frame, CancellationToken.None);
        var noisyBytes = embedded.Rgba.ToArray();
        for (var i = 0; i < noisyBytes.Length; i += 4)
        {
            var delta = ((i / 4) & 1) == 0 ? 1 : -1;
            noisyBytes[i] = (byte)Math.Clamp(noisyBytes[i] + delta, 0, 255);
            noisyBytes[i + 1] = (byte)Math.Clamp(noisyBytes[i + 1] - delta, 0, 255);
            noisyBytes[i + 2] = (byte)Math.Clamp(noisyBytes[i + 2] + delta, 0, 255);
        }

        var noisy = new PixelImage(embedded.Size, noisyBytes);
        var header = services.Carrier.ReadHeader(noisy, CancellationToken.None).Header;
        var key = services.Protocol.ResolveMappingKey(header, password: null);
        var data = services.Carrier.ReadData(noisy, header, key, CancellationToken.None);
        using var decoded = services.Protocol.DecodeData(header, data.EncodedData, password: null).Payload;

        Assert.Equal(payload.Bytes.ToArray(), decoded.Bytes.ToArray());
    }

    [Fact]
    public void 控制信道不足时写入在修改任何源像素前失败()
    {
        var services = CreateServices();
        var source = CreateTexturedImage(128, 128);
        var before = source.Rgba.ToArray();
        using var payload = new WatermarkPayload(Encoding.UTF8.GetBytes("too-small"), PayloadContentType.Text);
        var frame = services.Protocol.Encode(payload, EmbeddingProfileId.Stealth, password: null);

        Assert.Throws<InvalidOperationException>(() =>
            services.Carrier.Embed(source, frame, CancellationToken.None));
        Assert.Equal(before, source.Rgba.ToArray());
    }

    private static (WatermarkFrameProtocol Protocol, FrequencyWatermarkCarrier Carrier) CreateServices()
    {
        var errorCorrection = new ReedSolomonCodec();
        var protocol = new WatermarkFrameProtocol(errorCorrection, new DeterministicRandomSource());
        var carrier = new FrequencyWatermarkCarrier(new Dct8x8Transform(), protocol, errorCorrection);
        return (protocol, carrier);
    }

    private static PixelImage CreateTexturedImage(int width, int height)
        => new(new ImageSize(width, height), CreateTexturedRgba(width, height));

    private static byte[] CreateTexturedRgba(int width, int height)
    {
        var rgba = new byte[checked(width * height * 4)];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                // 组合渐变、棋盘纹理和互质步长，避免测试图退化为不现实的纯色载体。
                var checker = (((x / 8) + (y / 8)) & 1) == 0 ? 19 : -19;
                var offset = ((y * width) + x) * 4;
                rgba[offset] = Clamp(80 + ((x * 7 + y * 3) % 130) + checker);
                rgba[offset + 1] = Clamp(70 + ((x * 5 + y * 11) % 140) - checker);
                rgba[offset + 2] = Clamp(60 + ((x * 13 + y * 2) % 150) + (checker / 2));
                rgba[offset + 3] = 255;
            }
        }

        return rgba;
    }

    private static byte Clamp(int value) => (byte)Math.Clamp(value, 0, 255);

    private sealed class DeterministicRandomSource : IRandomSource
    {
        private uint _state = 0x1234ABCD;

        public void Fill(Span<byte> destination)
        {
            for (var i = 0; i < destination.Length; i++)
            {
                _state = (_state * 1_664_525) + 1_013_904_223;
                destination[i] = (byte)(_state >> 24);
            }
        }
    }
}
