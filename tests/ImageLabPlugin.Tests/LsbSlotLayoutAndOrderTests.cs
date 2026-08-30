using ImageLabPlugin.Domain.Imaging;
using ImageLabPlugin.Domain.Steganography;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class LsbSlotLayoutAndOrderTests
{
    [Fact]
    public void 混合Alpha只产生不透明像素且Rgb顺序固定为R到G到B()
    {
        var image = new PixelImage(new ImageSize(3, 1), [10, 11, 12, 255, 20, 21, 22, 128, 30, 31, 32, 255]);
        var layout = new LsbSlotLayout(image);

        Assert.Equal(2, layout.OpaquePixelCount);
        Assert.Equal(6, layout.GetEligibleSlotCount(LsbChannelStrategy.RgbRoundRobin));
        Assert.Equal((0, LsbChannel.Red, 0), Slot(layout.Resolve(0, LsbChannelStrategy.RgbRoundRobin)));
        Assert.Equal((0, LsbChannel.Green, 1), Slot(layout.Resolve(1, LsbChannelStrategy.RgbRoundRobin)));
        Assert.Equal((0, LsbChannel.Blue, 2), Slot(layout.Resolve(2, LsbChannelStrategy.RgbRoundRobin)));
        Assert.Equal((2, LsbChannel.Red, 8), Slot(layout.Resolve(3, LsbChannelStrategy.RgbRoundRobin)));
    }

    [Fact]
    public void SplitMix64符合版本化公开向量()
    {
        var random = new SplitMix64(0);
        Assert.Equal(0xe220a8397b1dcdafUL, random.Next());
        Assert.Equal(0x6e789e6aa1b965f4UL, random.Next());
        Assert.Equal(0x06c45d188009454fUL, random.Next());
    }

    [Fact]
    public void 伪随机位置相同Seed可复现且无重复无越界()
    {
        var order = new PseudoRandomLsbSlotOrder();
        var first = order.Select(1000, 900, 0x123456789abcdef0UL, CancellationToken.None);
        var second = order.Select(1000, 900, 0x123456789abcdef0UL, CancellationToken.None);
        var different = order.Select(1000, 900, 7, CancellationToken.None);

        Assert.Equal(first, second);
        Assert.NotEqual(first, different);
        Assert.Equal(900, first.Distinct().Count());
        Assert.All(first, value => Assert.InRange(value, 0, 999));
    }

    [Fact]
    public void 伪随机固定Seed前十个位置符合Golden()
    {
        Assert.Equal(new[] { 5, 9, 14, 15, 13, 4, 0, 18, 8, 11 },
            new PseudoRandomLsbSlotOrder().Select(20, 10, 1, CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    public void 两种位置策略都精确返回请求数量(int count)
    {
        ILsbSlotOrder[] orders = [new SequentialLsbSlotOrder(), new PseudoRandomLsbSlotOrder()];
        foreach (var order in orders) Assert.Equal(count, order.Select(100, count, 42, CancellationToken.None).Length);
    }

    private static (int Pixel, LsbChannel Channel, int Offset) Slot(LsbSlot slot) => (slot.PixelIndex, slot.Channel, slot.RgbaOffset);
}
