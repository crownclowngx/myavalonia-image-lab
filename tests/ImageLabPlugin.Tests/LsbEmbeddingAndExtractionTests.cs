using ImageLabPlugin.Domain.Imaging;
using ImageLabPlugin.Domain.Steganography;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class LsbEmbeddingAndExtractionTests
{
    public static IEnumerable<object[]> Recipes()
    {
        foreach (var channels in Enum.GetValues<LsbChannelStrategy>())
        foreach (var bit in new[] { 0, 1 })
        foreach (var placement in Enum.GetValues<LsbPlacementKind>())
            yield return [(int)channels, bit, (int)placement];
    }

    [Theory]
    [MemberData(nameof(Recipes))]
    public void 文本在四通道两位平面两位置策略均可逐字节回读(int channelValue, int bit, int placementValue)
    {
        var channels = (LsbChannelStrategy)channelValue;
        var placement = (LsbPlacementKind)placementValue;
        var source = CreateOpaque(32, 32);
        var original = source.Rgba.ToArray();
        var layout = new LsbSlotLayout(source);
        var recipe = new LsbRecipe(channels, bit, placement, 123456789);
        using var payload = LsbPayload.FromText("中文 LSB ✓");
        var frame = new LsbFrameCodec().Encode(payload);
        var orders = new ILsbSlotOrder[] { new SequentialLsbSlotOrder(), new PseudoRandomLsbSlotOrder() };

        var embedded = new LsbEmbeddingEngine(orders).Embed(source, layout, frame, recipe, CancellationToken.None);
        var extracted = new LsbExtractionEngine(new LsbFrameCodec(), orders).Extract(embedded.Image, layout, recipe, CancellationToken.None);

        Assert.Equal(LsbReadStatus.Success, extracted.Status);
        Assert.Equal("中文 LSB ✓", extracted.DecodeTextStrict());
        Assert.Equal(original, source.Rgba.ToArray());
        Assert.NotSame(source, embedded.Image);
        Assert.Equal(frame.Length * 8, embedded.Facts.SelectedLogicalSlots.Length);
        Assert.Equal(embedded.Facts.SelectedLogicalSlots.Length, embedded.Facts.ChangedSlots + embedded.Facts.UnchangedSlots);
    }

    [Fact]
    public void 跳过像素与Alpha逐字节不变且Bit1变化只能为正负2()
    {
        var bytes = Enumerable.Repeat((byte)0x55, 400 * 4).ToArray();
        for (var pixel = 0; pixel < 400; pixel++) bytes[(pixel * 4) + 3] = pixel == 10 ? (byte)128 : (byte)255;
        var source = new PixelImage(new ImageSize(20, 20), bytes);
        var layout = new LsbSlotLayout(source);
        using var payload = LsbPayload.FromText(string.Empty);
        var frame = new LsbFrameCodec().Encode(payload);
        ILsbSlotOrder[] orders = [new SequentialLsbSlotOrder(), new PseudoRandomLsbSlotOrder()];

        var result = new LsbEmbeddingEngine(orders).Embed(source, layout, frame, new(LsbChannelStrategy.RgbRoundRobin, 1, LsbPlacementKind.Sequential, 0), CancellationToken.None);

        var before = source.Rgba.Span;
        var after = result.Image.Rgba.Span;
        for (var offset = 0; offset < before.Length; offset++)
        {
            if (offset / 4 == 10 || offset % 4 == 3) Assert.Equal(before[offset], after[offset]);
            else Assert.Contains(after[offset] - before[offset], new[] { -2, 0, 2 });
        }
    }

    [Fact]
    public void 参数错配返回结构化失败而不猜测()
    {
        var source = CreateOpaque(32, 32);
        var layout = new LsbSlotLayout(source);
        using var payload = LsbPayload.FromText("secret");
        var frame = new LsbFrameCodec().Encode(payload);
        ILsbSlotOrder[] orders = [new SequentialLsbSlotOrder(), new PseudoRandomLsbSlotOrder()];
        var result = new LsbEmbeddingEngine(orders).Embed(source, layout, frame, new(LsbChannelStrategy.Red, 0, LsbPlacementKind.PseudoRandom, 100), CancellationToken.None);

        var wrong = new LsbExtractionEngine(new LsbFrameCodec(), orders).Extract(result.Image, layout, new(LsbChannelStrategy.Red, 0, LsbPlacementKind.PseudoRandom, 101), CancellationToken.None);

        Assert.NotEqual(LsbReadStatus.Success, wrong.Status);
        Assert.Null(wrong.Payload);
    }

    [Fact]
    public void 像素探针区分Header已变化与未选择并报告原始坐标事实()
    {
        var source = CreateOpaque(20, 20);
        var layout = new LsbSlotLayout(source);
        using var payload = LsbPayload.FromText(string.Empty);
        var frame = new LsbFrameCodec().Encode(payload);
        ILsbSlotOrder[] orders = [new SequentialLsbSlotOrder(), new PseudoRandomLsbSlotOrder()];
        var recipe = new LsbRecipe(LsbChannelStrategy.Red, 0, LsbPlacementKind.Sequential, 0);
        var embedded = new LsbEmbeddingEngine(orders).Embed(source, layout, frame, recipe, CancellationToken.None);

        var selected = new LsbPixelInspector().Inspect(source, embedded.Image, layout, recipe, embedded.Facts, 0, 0);
        var unselected = new LsbPixelInspector().Inspect(source, embedded.Image, layout, recipe, embedded.Facts, 19, 19);

        Assert.True(selected.IsEligible);
        Assert.Equal(0, selected.Channels.Single().FrameBitIndex);
        Assert.Contains(selected.Channels.Single().State, new[] { LsbProbeSelectionState.HeaderChanged, LsbProbeSelectionState.HeaderUnchanged });
        Assert.Equal(LsbProbeSelectionState.NotSelected, unselected.Channels.Single().State);
        Assert.Equal(source.GetPixel(19, 19).R, unselected.Cover.Red);
    }

    private static PixelImage CreateOpaque(int width, int height)
    {
        var bytes = new byte[width * height * 4];
        for (var pixel = 0; pixel < width * height; pixel++)
        {
            bytes[pixel * 4] = (byte)(pixel * 17);
            bytes[(pixel * 4) + 1] = (byte)(pixel * 31);
            bytes[(pixel * 4) + 2] = (byte)(pixel * 47);
            bytes[(pixel * 4) + 3] = 255;
        }
        return new(new(width, height), bytes);
    }
}
