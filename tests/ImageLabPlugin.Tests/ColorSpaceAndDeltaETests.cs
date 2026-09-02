using ImageLabPlugin.Domain.ColorTransfer;
using ImageLabPlugin.Domain.Shared.Imaging;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class ColorSpaceAndDeltaETests
{
    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(255, 1.0)]
    [InlineData(128, 0.21586050011389926)]
    public void Srgb分段解码符合Golden(int value, double expected)
    {
        var core = ColorTransferTestFactory.Create();
        var result = core.Srgb.Decode(SrgbColor.FromBytes((byte)value, 0, 0));
        Assert.Equal(expected, result.Red, 12);
    }

    [Fact]
    public void 白色映射到D65白点与Lab一百零零()
    {
        var core = ColorTransferTestFactory.Create();
        var xyz = core.Srgb.ToXyz(core.Srgb.Decode(new SrgbColor(1, 1, 1)));
        var lab = core.Lab.ToLab(xyz);
        Assert.Equal(CieLabColorSpace.WhiteX, xyz.X, 6); Assert.Equal(1d, xyz.Y, 6); Assert.Equal(CieLabColorSpace.WhiteZ, xyz.Z, 6);
        Assert.Equal(100d, lab.L, 4); Assert.Equal(0d, lab.A, 3); Assert.Equal(0d, lab.B, 3);
    }

    [Theory]
    [InlineData(255, 0, 0, 0)]
    [InlineData(255, 255, 0, 60)]
    [InlineData(0, 255, 0, 120)]
    [InlineData(0, 255, 255, 180)]
    [InlineData(0, 0, 255, 240)]
    [InlineData(255, 0, 255, 300)]
    public void Hsv六原色角度准确(int r, int g, int b, double hue)
    {
        var result = ColorTransferTestFactory.Create().Hsv.ToHsv(SrgbColor.FromBytes((byte)r, (byte)g, (byte)b));
        Assert.Equal(HueStatus.Defined, result.HueStatus); Assert.Equal(hue, result.HueDegrees, 10);
        Assert.Equal(1d, result.Saturation, 10); Assert.Equal(1d, result.Value, 10);
    }

    [Fact]
    public void 灰阶Hue明确为N且不会伪装成红色()
    {
        var result = ColorTransferTestFactory.Create().Hsv.ToHsv(SrgbColor.FromBytes(127, 127, 127));
        Assert.Equal(HueStatus.Undefined, result.HueStatus); Assert.Equal(0d, result.Saturation);
    }

    [Theory]
    [InlineData(50, 2.6772, -79.7751, 50, 0, -82.7485, 2.0425)]
    [InlineData(50, 3.1571, -77.2803, 50, 0, -82.7485, 2.8615)]
    [InlineData(50, 2.8361, -74.0200, 50, 0, -82.7485, 3.4412)]
    [InlineData(50, -1.3802, -84.2814, 50, 0, -82.7485, 1.0000)]
    public void Ciede2000符合Sharma公开参考对(double l1, double a1, double b1, double l2, double a2, double b2, double expected)
    {
        var delta = ColorTransferTestFactory.Create().Delta;
        var value = delta.Ciede2000(new CieLabColor(l1, a1, b1), new CieLabColor(l2, a2, b2));
        Assert.Equal(expected, value, 4);
        Assert.Equal(value, delta.Ciede2000(new CieLabColor(l2, a2, b2), new CieLabColor(l1, a1, b1)), 10);
    }

    [Fact]
    public void Lab往返固定网格保持字节()
    {
        var core = ColorTransferTestFactory.Create();
        foreach (var value in new byte[] { 0, 1, 12, 64, 128, 200, 254, 255 })
        {
            var input = SrgbColor.FromBytes(value, (byte)(255 - value), 128);
            var lab = core.Lab.ToLab(core.Srgb.ToXyz(core.Srgb.Decode(input)));
            var output = core.Srgb.Encode(core.Srgb.FromXyz(core.Lab.FromLab(lab))).ToBytes();
            Assert.Equal(input.ToBytes(), output);
        }
    }

    [Fact]
    public void 色域映射保持L与色相方向并产生结构化诊断()
    {
        var core = ColorTransferTestFactory.Create(); var input = new CieLabColor(60, 180, 90);
        var result = core.Gamut.Map(input);
        Assert.Equal(GamutMappingKind.ChromaCompressed, result.Kind); Assert.Equal(input.L, result.MappedLab.L, 8);
        Assert.Equal(input.B / input.A, result.MappedLab.B / result.MappedLab.A, 6); Assert.True(result.DeltaE76 > 0);
    }
}
