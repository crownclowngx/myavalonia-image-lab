using ImageLabPlugin.Domain.BitPlanes;
using ImageLabPlugin.Domain.Imaging;
using Xunit;

namespace ImageLabPlugin.Tests;

/// <summary>锁定位序、量化、统计、投影、探针和五通道重建的确定值。</summary>
public sealed class BitPlaneDomainTests
{
    [Theory]
    [InlineData(0, 0x01)]
    [InlineData(7, 0x80)]
    public void 单位掩码符合最低位与最高位权重(int bit, int expected) =>
        Assert.Equal(expected, BitMask8.Single(bit).Value);

    [Theory]
    [InlineData(0, 0xFF)]
    [InlineData(4, 0xF0)]
    [InlineData(7, 0x80)]
    public void 仅高位预设包含边界(int minimum, int expected) =>
        Assert.Equal(expected, BitMask8.KeepHigh(minimum).Value);

    [Theory]
    [InlineData(0, 0x01)]
    [InlineData(3, 0x0F)]
    [InlineData(7, 0xFF)]
    public void 仅低位预设包含边界(int maximum, int expected) =>
        Assert.Equal(expected, BitMask8.KeepLow(maximum).Value);

    [Fact]
    public void 非法位索引在统一值对象边界拒绝()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BitMask8.Single(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => BitMask8.KeepHigh(8));
        Assert.Throws<ArgumentOutOfRangeException>(() => BitMask8.KeepLow(8));
    }

    [Theory]
    [InlineData(0x00, "00000000")]
    [InlineData(0x01, "00000001")]
    [InlineData(0x7F, "01111111")]
    [InlineData(0x80, "10000000")]
    [InlineData(0xAA, "10101010")]
    [InlineData(0x55, "01010101")]
    [InlineData(0xFE, "11111110")]
    [InlineData(0xFF, "11111111")]
    public void Golden字节按bit7到bit0显示(int value, string expected)
    {
        var actual = string.Concat(Enumerable.Range(0, 8).Reverse()
            .Select(bit => (((byte)value >> bit) & 1).ToString()));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void 五通道抽取保持RGBA并按中点取偶量化Y()
    {
        var source = Image([255, 0, 0, 7, 0, 255, 0, 8, 0, 0, 255, 9, 128, 128, 128, 10], 4, 1);
        var extractor = new BitPlaneChannelExtractor();

        Assert.Equal(new byte[] { 255, 0, 0, 128 }, extractor.Extract(source, BitPlaneChannel.Red).Values.ToArray());
        Assert.Equal(new byte[] { 7, 8, 9, 10 }, extractor.Extract(source, BitPlaneChannel.Alpha).Values.ToArray());
        Assert.Equal(new byte[] { 76, 150, 29, 128 }, extractor.Extract(source, BitPlaneChannel.Luma).Values.ToArray());
        Assert.Equal((byte)0, YCbCrColorSpace.QuantizeLuma(0, 0, 0));
        Assert.Equal((byte)255, YCbCrColorSpace.QuantizeLuma(255, 255, 255));
    }

    [Fact]
    public void BytePlane复制输入且拒绝尺寸不一致()
    {
        var input = new byte[] { 0xAA };
        var plane = new BytePlane(new ImageSize(1, 1), BitPlaneChannel.Red, input);
        input[0] = 0;
        Assert.Equal(0xAA, plane[0, 0]);
        Assert.Throws<ArgumentException>(() => new BytePlane(new ImageSize(2, 1), BitPlaneChannel.Red, input));
    }

    [Fact]
    public void 统计一次覆盖八位且零一计数与熵正确()
    {
        var plane = new BytePlane(new ImageSize(2, 1), BitPlaneChannel.Red, [0x00, 0xFF]);
        var rows = new BitPlaneStatisticsAnalyzer().Analyze(plane);
        Assert.Equal(8, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Equal(1, row.ZeroCount); Assert.Equal(1, row.OneCount);
            Assert.Equal(0.5d, row.OneRatio, 12); Assert.Equal(1d, row.BinaryEntropy, 12);
        });
    }

    [Fact]
    public void 全零与全一的二元熵为零()
    {
        var zero = new BytePlane(new ImageSize(1, 1), BitPlaneChannel.Red, [0]);
        var full = new BytePlane(new ImageSize(1, 1), BitPlaneChannel.Red, [255]);
        Assert.All(new BitPlaneStatisticsAnalyzer().Analyze(zero), row => Assert.Equal(0d, row.BinaryEntropy));
        Assert.All(new BitPlaneStatisticsAnalyzer().Analyze(full), row => Assert.Equal(0d, row.BinaryEntropy));
    }

    [Fact]
    public void 单位平面恒不透明且组合图不拉伸低位()
    {
        var source = Image([1, 2, 3, 0], 1, 1);
        var plane = new BytePlane(source.Size, BitPlaneChannel.Red, [1]);
        var result = new BitPlaneProjector().Project(source, plane, BitMask8.Single(0), 0);
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, result.FocusedPlane.Rgba.ToArray());
        Assert.Equal(new byte[] { 1, 1, 1, 255 }, result.CombinedPlane.Rgba.ToArray());
    }

    [Theory]
    [InlineData(0x80, 0x80)]
    [InlineData(0x0F, 0x0A)]
    [InlineData(0xF0, 0xA0)]
    [InlineData(0x55, 0x00)]
    [InlineData(0xAA, 0xAA)]
    public void 组合灰度严格等于字节与掩码(int mask, int expected)
    {
        var source = Image([0xAA, 0, 0, 255], 1, 1);
        var plane = new BytePlane(source.Size, BitPlaneChannel.Red, [0xAA]);
        var result = new BitPlaneProjector().Project(source, plane, new BitMask8((byte)mask), 7);
        Assert.Equal(new byte[] { (byte)expected, (byte)expected, (byte)expected, 255 }, result.CombinedPlane.Rgba.ToArray());
    }

    [Fact]
    public void 预览最大边受控小图不放大且首末坐标一致()
    {
        var large = BitPlanePreviewMap.Create(new ImageSize(2000, 1000));
        Assert.Equal((1024, 512), (large.PreviewSize.Width, large.PreviewSize.Height));
        Assert.Equal((0, 0), large.GetSourcePoint(0, 0));
        var last = large.GetSourcePoint(1023, 511);
        Assert.Equal((1999, 999), last);
        var small = BitPlanePreviewMap.Create(new ImageSize(3, 2));
        Assert.Equal(new ImageSize(3, 2), small.PreviewSize);
    }

    [Fact]
    public void RGB重建只替换所选通道且不修改源图()
    {
        var source = Image([0xAB, 0xCD, 0xEF, 0x7F], 1, 1);
        var plane = new BitPlaneChannelExtractor().Extract(source, BitPlaneChannel.Red);
        var result = new BitPlaneReconstructor().Reconstruct(source, plane, new BitMask8(0xF0));
        Assert.Equal(new byte[] { 0xA0, 0xCD, 0xEF, 0x7F }, result.Image.Rgba.ToArray());
        Assert.Equal(new byte[] { 0xAB, 0xCD, 0xEF, 0x7F }, source.Rgba.ToArray());
    }

    [Fact]
    public void Alpha重建只替换Alpha并保留透明像素隐藏RGB()
    {
        var source = Image([11, 22, 33, 0xAF], 1, 1);
        var plane = new BitPlaneChannelExtractor().Extract(source, BitPlaneChannel.Alpha);
        var result = new BitPlaneReconstructor().Reconstruct(source, plane, new BitMask8(0x0F));
        Assert.Equal(new byte[] { 11, 22, 33, 0x0F }, result.Image.Rgba.ToArray());
    }

    [Fact]
    public void 全掩码对五通道均逐字节恒等()
    {
        var source = Image([17, 99, 241, 3, 255, 1, 128, 200], 2, 1);
        var extractor = new BitPlaneChannelExtractor();
        var reconstructor = new BitPlaneReconstructor();
        foreach (var channel in Enum.GetValues<BitPlaneChannel>())
        {
            var plane = extractor.Extract(source, channel);
            var result = reconstructor.Reconstruct(source, plane, new BitMask8(0xFF));
            Assert.Equal(source.Rgba.ToArray(), result.Image.Rgba.ToArray());
            Assert.Equal(0, result.ClippedPixelCount);
        }
    }

    [Fact]
    public void 探针报告原始RGBA二进制掩码和保留值()
    {
        var source = Image([0xAA, 2, 3, 4], 1, 1);
        var plane = new BitPlaneChannelExtractor().Extract(source, BitPlaneChannel.Red);
        var report = new BitPlanePixelInspector().Inspect(source, plane, new BitMask8(0x0F), 0, 0);
        Assert.Equal((0xAA, "0b10101010", 0x0A), (report.ChannelValue, report.BinaryValue, report.KeptValue));
        Assert.Equal((2, 3, 4), (report.Green, report.Blue, report.Alpha));
    }

    [Fact]
    public void 已取消操作不会进入长循环()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var source = Image([1, 2, 3, 4], 1, 1);
        Assert.Throws<OperationCanceledException>(() => new BitPlaneChannelExtractor().Extract(source, BitPlaneChannel.Red, cancellation.Token));
        var plane = new BytePlane(source.Size, BitPlaneChannel.Red, [1]);
        Assert.Throws<OperationCanceledException>(() => new BitPlaneStatisticsAnalyzer().Analyze(plane, cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => new BitPlaneProjector().Project(source, plane, new BitMask8(1), 0, cancellationToken: cancellation.Token));
    }

    private static PixelImage Image(byte[] rgba, int width, int height) => new(new ImageSize(width, height), rgba);
}
