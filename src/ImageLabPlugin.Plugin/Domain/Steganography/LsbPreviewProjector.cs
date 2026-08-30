using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Domain.Steganography;

internal sealed record LsbPreviewProjection(PixelImage Placement, PixelImage BitBefore, PixelImage BitAfter);

/// <summary>把完整尺寸位置事实聚合到最大边受限的代理图，避免 Document 长期持有多张全图。</summary>
internal sealed class LsbPreviewProjector
{
    public LsbPreviewProjection Project(PixelImage cover, PixelImage stego, LsbSlotLayout layout, LsbRecipe recipe, LsbEmbeddingFacts facts, int maximumEdge, CancellationToken token)
    {
        if (maximumEdge <= 0) throw new ArgumentOutOfRangeException(nameof(maximumEdge));
        var scale = Math.Min(1d, maximumEdge / (double)Math.Max(cover.Size.Width, cover.Size.Height));
        var width = Math.Max(1, (int)Math.Round(cover.Size.Width * scale));
        var height = Math.Max(1, (int)Math.Round(cover.Size.Height * scale));
        var size = new ImageSize(width, height);
        var placement = new byte[checked(width * height * 4)];
        var before = new long[width * height];
        var after = new long[width * height];
        var count = new long[width * height];
        var coverBytes = cover.Rgba.Span;
        var stegoBytes = stego.Rgba.Span;
        for (var bitIndex = 0; bitIndex < facts.SelectedLogicalSlots.Length; bitIndex++)
        {
            if ((bitIndex & 0x3fff) == 0) token.ThrowIfCancellationRequested();
            var slot = layout.Resolve(facts.SelectedLogicalSlots[bitIndex], recipe.Channels);
            var x = slot.PixelIndex % cover.Size.Width;
            var y = slot.PixelIndex / cover.Size.Width;
            var px = Math.Min(width - 1, x * width / cover.Size.Width);
            var py = Math.Min(height - 1, y * height / cover.Size.Height);
            var cell = (py * width) + px;
            count[cell]++;
            before[cell] += (coverBytes[slot.RgbaOffset] >> recipe.BitPlane) & 1;
            after[cell] += (stegoBytes[slot.RgbaOffset] >> recipe.BitPlane) & 1;
            var offset = cell * 4;
            var changed = coverBytes[slot.RgbaOffset] != stegoBytes[slot.RgbaOffset];
            if (changed) { placement[offset] = 220; placement[offset + 1] = 55; placement[offset + 2] = 47; }
            else if (bitIndex < LsbFrameCodec.HeaderLength * 8) { placement[offset] = 37; placement[offset + 1] = 99; placement[offset + 2] = 235; }
            else if (placement[offset + 3] == 0) { placement[offset] = 234; placement[offset + 1] = 179; placement[offset + 2] = 8; }
            placement[offset + 3] = 255;
        }
        var beforeRgba = new byte[placement.Length];
        var afterRgba = new byte[placement.Length];
        for (var cell = 0; cell < count.Length; cell++)
        {
            var offset = cell * 4;
            if (placement[offset + 3] == 0) { placement[offset] = placement[offset + 1] = placement[offset + 2] = 28; placement[offset + 3] = 255; }
            var beforeValue = count[cell] == 0 ? (byte)32 : (byte)Math.Round(255d * before[cell] / count[cell]);
            var afterValue = count[cell] == 0 ? (byte)32 : (byte)Math.Round(255d * after[cell] / count[cell]);
            beforeRgba[offset] = beforeRgba[offset + 1] = beforeRgba[offset + 2] = beforeValue; beforeRgba[offset + 3] = 255;
            afterRgba[offset] = afterRgba[offset + 1] = afterRgba[offset + 2] = afterValue; afterRgba[offset + 3] = 255;
        }
        return new(new(size, placement), new(size, beforeRgba), new(size, afterRgba));
    }
}
