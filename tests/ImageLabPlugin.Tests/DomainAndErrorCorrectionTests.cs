using ImageLabPlugin.Domain.Shared.Spectral;
using ImageLabPlugin.Domain.Shared.Analysis;
using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Domain.Watermarking;
using ImageLabPlugin.Infrastructure.ErrorCorrection;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class DomainAndErrorCorrectionTests
{
    [Fact]
    public void 图像尺寸在分配前拒绝非法值和过大图片()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageSize(0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageSize(10, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageSize(100_000, 100_000));
    }

    [Fact]
    public void Dct与逆变换能够在浮点误差内恢复八乘八块()
    {
        var transform = new Dct8x8Transform();
        var spatial = Enumerable.Range(0, 64).Select(index => 40d + (index * 2.5d)).ToArray();
        var frequency = new double[64];
        var restored = new double[64];

        transform.Forward(spatial, frequency);
        transform.Inverse(frequency, restored);

        for (var i = 0; i < spatial.Length; i++)
        {
            Assert.InRange(Math.Abs(spatial[i] - restored[i]), 0d, 1e-9);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Qim能够写入并读回两个比特集合(bool bit)
    {
        var embedded = QimModulator.Embed(17.3d, bit, 28d);
        var decision = QimModulator.Read(embedded, 28d);

        Assert.Equal(bit, decision.Bit);
        Assert.Equal(1d, decision.Confidence, 8);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(17)]
    [InlineData(223)]
    [InlineData(224)]
    [InlineData(1024)]
    public void ReedSolomon支持完整块和缩短块往返(int length)
    {
        var codec = new ReedSolomonCodec();
        var source = Enumerable.Range(0, length).Select(index => (byte)((index * 37) & 0xFF)).ToArray();

        var encoded = codec.Encode(source);
        var decoded = codec.Decode(encoded, source.Length);

        Assert.Equal(codec.GetEncodedLength(length), encoded.Length);
        Assert.Equal(source, decoded.Data);
        Assert.Equal(0, decoded.CorrectedSymbols);
    }

    [Fact]
    public void ReedSolomon能够修复单块十六个以内的符号错误()
    {
        var codec = new ReedSolomonCodec();
        var source = Enumerable.Range(0, 223).Select(index => (byte)index).ToArray();
        var encoded = codec.Encode(source);
        for (var i = 0; i < 16; i++)
        {
            encoded[i * 13] ^= (byte)(0x31 + i);
        }

        var decoded = codec.Decode(encoded, source.Length);

        Assert.Equal(source, decoded.Data);
        Assert.Equal(16, decoded.CorrectedSymbols);
    }

    [Fact]
    public void 差异和频谱投影保持尺寸且不修改输入()
    {
        var rgba = Enumerable.Repeat(new byte[] { 80, 120, 160, 255 }, 64 * 64)
            .SelectMany(pixel => pixel)
            .ToArray();
        var original = new PixelImage(new ImageSize(64, 64), rgba);
        var modified = original.Clone();
        modified.SetRgb(8, 8, 100, 110, 170);
        var before = original.Rgba.ToArray();

        var difference = ImageDifferenceProjector.Create(original, modified);
        var spectrum = new FrequencySpectrumProjector(new Dct8x8Transform())
            .Create(modified, CancellationToken.None);

        Assert.Equal(original.Size, difference.Size);
        Assert.Equal(original.Size, spectrum.Size);
        Assert.Equal(before, original.Rgba.ToArray());
        Assert.Contains(difference.Rgba.ToArray(), value => value > 0);
        Assert.Contains(spectrum.Rgba.ToArray(), value => value > 0);
    }

    [Fact]
    public void 分析预览会保持比例并限制最大边长()
    {
        var source = new PixelImage(new ImageSize(2000, 1000), new byte[2000 * 1000 * 4]);

        var preview = ImagePreviewProjector.FitWithin(source, 1000);

        Assert.Equal(new ImageSize(1000, 500), preview.Size);
    }
}
