namespace ImageLabPlugin.Domain.Shared.Imaging;

/// <summary>频域分析可选择的六个颜色通道。</summary>
internal enum ImageChannel
{
    Red,
    Green,
    Blue,
    Luma,
    ChromaBlue,
    ChromaRed
}

/// <summary>拥有一份连续、只读语义的通道样本。</summary>
/// <remarks>
/// 通道平面故意不暴露可写内存。FFT 的补零和重建必须复制到自己的工作区，避免一次分析意外修改
/// 后续 DCT、频点查询或另一个 Document 所观察到的事实。
/// </remarks>
internal sealed class ImageChannelPlane
{
    private readonly double[] _values;

    public ImageChannelPlane(ImageSize size, ImageChannel channel, ReadOnlySpan<double> values)
    {
        if (values.Length != size.PixelCount)
        {
            throw new ArgumentException("通道样本数必须与图片像素数一致。", nameof(values));
        }

        Size = size;
        Channel = channel;
        _values = values.ToArray();
    }

    public ImageSize Size { get; }
    public ImageChannel Channel { get; }
    public ReadOnlyMemory<double> Values => _values;

    public double this[int x, int y]
    {
        get
        {
            if ((uint)x >= (uint)Size.Width || (uint)y >= (uint)Size.Height)
            {
                throw new ArgumentOutOfRangeException(nameof(x), $"通道坐标 ({x},{y}) 超出 {Size.Width}×{Size.Height}。 ");
            }

            return _values[(y * Size.Width) + x];
        }
    }
}

/// <summary>选定通道合成回 RGBA 后的裁切报告。</summary>
internal sealed record ChannelReconstructionResult(
    PixelImage Image,
    int ClippedPixelCount,
    int ClippedComponentCount);
