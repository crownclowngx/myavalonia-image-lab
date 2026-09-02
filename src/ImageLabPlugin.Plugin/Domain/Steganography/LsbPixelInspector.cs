using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Domain.Steganography;

/// <summary>按原图坐标解释选中槽、Frame 区域、消息 bit、前后 bit 和字节差。</summary>
/// <remarks>探针只在用户请求的单个坐标上扫描受限位置数组，不建立逐像素对象图或长期反向索引。</remarks>
internal sealed class LsbPixelInspector
{
    public LsbPixelProbe Inspect(PixelImage cover, PixelImage stego, LsbSlotLayout layout, LsbRecipe recipe, LsbEmbeddingFacts facts, int x, int y)
    {
        if (cover.Size != stego.Size || cover.Size != layout.Size) throw new ArgumentException("探针要求同尺寸 cover/stego 和同一布局。");
        var coverPixel = cover.GetPixel(x, y);
        var stegoPixel = stego.GetPixel(x, y);
        var pixelIndex = checked((y * cover.Size.Width) + x);
        var eligible = coverPixel.A == byte.MaxValue;
        var channels = recipe.Channels == LsbChannelStrategy.RgbRoundRobin
            ? new[] { LsbChannel.Red, LsbChannel.Green, LsbChannel.Blue }
            : new[] { recipe.Channels switch { LsbChannelStrategy.Red => LsbChannel.Red, LsbChannelStrategy.Green => LsbChannel.Green, _ => LsbChannel.Blue } };
        var result = new List<LsbProbeChannelFact>(channels.Length);
        foreach (var channel in channels)
        {
            var logical = layout.TryGetLogicalIndex(pixelIndex, channel, recipe.Channels);
            if (logical is null)
            {
                result.Add(new(channel, LsbProbeSelectionState.NotEligible, null, null, 0, 0, 0));
                continue;
            }
            var frameBit = Array.IndexOf(facts.SelectedLogicalSlots, logical.Value);
            var beforeValue = channel switch { LsbChannel.Red => coverPixel.R, LsbChannel.Green => coverPixel.G, _ => coverPixel.B };
            var afterValue = channel switch { LsbChannel.Red => stegoPixel.R, LsbChannel.Green => stegoPixel.G, _ => stegoPixel.B };
            var beforeBit = (beforeValue >> recipe.BitPlane) & 1;
            var afterBit = (afterValue >> recipe.BitPlane) & 1;
            if (frameBit < 0)
            {
                result.Add(new(channel, LsbProbeSelectionState.NotSelected, null, null, beforeBit, afterBit, afterValue - beforeValue));
                continue;
            }
            var header = frameBit < LsbFrameCodec.HeaderLength * 8;
            var changed = beforeValue != afterValue;
            var state = (header, changed) switch
            {
                (true, true) => LsbProbeSelectionState.HeaderChanged,
                (true, false) => LsbProbeSelectionState.HeaderUnchanged,
                (false, true) => LsbProbeSelectionState.PayloadChanged,
                _ => LsbProbeSelectionState.PayloadUnchanged
            };
            result.Add(new(channel, state, frameBit, afterBit, beforeBit, afterBit, afterValue - beforeValue));
        }
        return new(x, y, new(coverPixel.R, coverPixel.G, coverPixel.B, coverPixel.A), new(stegoPixel.R, stegoPixel.G, stegoPixel.B, stegoPixel.A), eligible, result);
    }
}
