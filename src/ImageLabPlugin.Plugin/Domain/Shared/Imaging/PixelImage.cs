namespace ImageLabPlugin.Domain.Shared.Imaging;

/// <summary>以紧凑 RGBA8888 表示一张已解码图片。</summary>
/// <remarks>
/// 领域层只认识确定的像素数组，不认识 PNG、JPEG、Avalonia Bitmap 或文件路径。构造函数复制输入，
/// 防止编解码器释放或复用缓冲区后改变领域事实；算法内部需要可写副本时显式调用 <see cref="Clone"/>。
/// </remarks>
internal sealed class PixelImage
{
    private readonly byte[] _rgba;

    public PixelImage(ImageSize size, ReadOnlySpan<byte> rgba)
    {
        var expected = checked((int)(size.PixelCount * 4));
        if (rgba.Length != expected)
        {
            throw new ArgumentException($"RGBA 数据长度应为 {expected}，实际为 {rgba.Length}。", nameof(rgba));
        }

        Size = size;
        _rgba = rgba.ToArray();
    }

    private PixelImage(ImageSize size, byte[] ownedRgba)
    {
        Size = size;
        _rgba = ownedRgba;
    }

    public ImageSize Size { get; }
    public ReadOnlyMemory<byte> Rgba => _rgba;
    internal Span<byte> WritableRgba => _rgba;

    public PixelImage Clone() => new(Size, (byte[])_rgba.Clone());

    public byte GetAlpha(int x, int y) => _rgba[GetOffset(x, y) + 3];

    public (byte R, byte G, byte B, byte A) GetPixel(int x, int y)
    {
        var offset = GetOffset(x, y);
        return (_rgba[offset], _rgba[offset + 1], _rgba[offset + 2], _rgba[offset + 3]);
    }

    public void SetRgb(int x, int y, byte red, byte green, byte blue)
    {
        var offset = GetOffset(x, y);
        _rgba[offset] = red;
        _rgba[offset + 1] = green;
        _rgba[offset + 2] = blue;
    }

    private int GetOffset(int x, int y)
    {
        if ((uint)x >= (uint)Size.Width || (uint)y >= (uint)Size.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(x), $"像素坐标 ({x},{y}) 超出 {Size.Width}×{Size.Height} 图片。");
        }

        return checked(((y * Size.Width) + x) * 4);
    }
}
