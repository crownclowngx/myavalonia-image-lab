using ImageLabPlugin.Domain.Imaging;
using ImageLabPlugin.Domain.SeamCarving;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class SeamEnergyTests
{
    private readonly SeamLumaProjector _luma = new();

    [Theory]
    [InlineData(0, 0, 0, 255, 0d)]
    [InlineData(255, 255, 255, 255, 255d)]
    [InlineData(255, 0, 0, 255, 76.245d)]
    [InlineData(0, 255, 0, 255, 149.685d)]
    [InlineData(0, 0, 255, 255, 29.07d)]
    [InlineData(12, 34, 56, 0, 255d)]
    public void 白底Alpha与Bt601亮度符合Golden(byte r, byte g, byte b, byte a, double expected)
    {
        var image = Image(1, 1, r, g, b, a);
        Assert.Equal(expected, _luma.Project(image)[0], 10);
    }

    [Fact]
    public void 半透明像素先合成白底再求亮度()
    {
        var image = Image(1, 1, 0, 0, 0, 128);
        var expected = 255d * (1d - (128d / 255d));
        Assert.Equal(expected, _luma.Project(image)[0], 10);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 5)]
    [InlineData(5, 1)]
    [InlineData(2, 2)]
    public void 常量图在所有窄边界上Sobel为零(int width, int height)
    {
        var image = Solid(width, height, 80, 90, 100, 255);
        var map = Calculator().Calculate(image, new SeamMask(image.Size));
        Assert.All(map.BaseEnergy.ToArray(), value => Assert.Equal(0d, value));
        Assert.Equal(0d, map.Summary.Maximum);
        Assert.Equal(0, map.Summary.NonFiniteCount);
    }

    [Fact]
    public void 垂直阶跃只产生水平梯度且能量归一化有界()
    {
        var pixels = new byte[3 * 3 * 4];
        for (var y = 0; y < 3; y++)
        for (var x = 0; x < 3; x++)
        {
            var value = x == 0 ? (byte)0 : (byte)255;
            var offset = ((y * 3) + x) * 4;
            pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = value; pixels[offset + 3] = 255;
        }
        var map = Calculator().Calculate(new PixelImage(new ImageSize(3, 3), pixels), new SeamMask(new ImageSize(3, 3)));
        Assert.Equal(1d / Math.Sqrt(2d), map.GetBase(1, 1), 10);
        Assert.All(map.BaseEnergy.ToArray(), value => Assert.InRange(value, 0d, 1d));
    }

    [Fact]
    public void 三态蒙版施加固定有限偏置且互斥()
    {
        var image = Solid(3, 1, 50, 50, 50, 255);
        var mask = new SeamMask(image.Size);
        mask.Set(0, 0, SeamMaskValue.Protect);
        mask.Set(1, 0, SeamMaskValue.PreferRemoval);
        mask.Set(2, 0, SeamMaskValue.Protect);
        mask.Set(2, 0, SeamMaskValue.Normal);
        var map = Calculator().Calculate(image, mask);
        Assert.Equal(1000d, map.GetEffective(0, 0));
        Assert.Equal(-1000d, map.GetEffective(1, 0));
        Assert.Equal(0d, map.GetEffective(2, 0));
    }

    [Fact]
    public void 线性与对数只改变显示不改变领域能量()
    {
        var image = Image(2, 2, 0, 0, 0, 255, 255, 255, 255, 255,
            0, 0, 0, 255, 255, 255, 255, 255);
        var map = Calculator().Calculate(image, new SeamMask(image.Size));
        var before = map.BaseEnergy.ToArray();
        var projector = new SeamEnergyPreviewProjector();
        var linear = projector.Project(map, false, EnergyDisplayMode.Linear);
        var logarithmic = projector.Project(map, false, EnergyDisplayMode.Logarithmic);
        Assert.Equal(before, map.BaseEnergy.ToArray());
        Assert.Equal(image.Size, linear.Size);
        Assert.Equal(image.Size, logarithmic.Size);
    }

    [Fact]
    public void 重复运行能量逐Double相同()
    {
        var image = Image(2, 2, 10, 20, 30, 40, 50, 60, 70, 80,
            90, 100, 110, 120, 130, 140, 150, 160);
        var calculator = Calculator();
        var first = calculator.Calculate(image, new SeamMask(image.Size));
        var second = calculator.Calculate(image, new SeamMask(image.Size));
        Assert.Equal(first.BaseEnergy.ToArray(), second.BaseEnergy.ToArray());
        Assert.Equal(first.EffectiveEnergy.ToArray(), second.EffectiveEnergy.ToArray());
    }

    internal static SobelEnergyCalculator Calculator() => new(new SeamLumaProjector());

    internal static PixelImage Solid(int width, int height, byte r, byte g, byte b, byte a)
    {
        var bytes = new byte[width * height * 4];
        for (var index = 0; index < bytes.Length; index += 4)
        { bytes[index] = r; bytes[index + 1] = g; bytes[index + 2] = b; bytes[index + 3] = a; }
        return new PixelImage(new ImageSize(width, height), bytes);
    }

    internal static PixelImage Image(int width, int height, params byte[] rgba) =>
        new(new ImageSize(width, height), rgba);
}
