namespace ImageLabPlugin.Domain.Shared.Imaging;

/// <summary>保存 YCbCr 中的亮度平面；V1 只修改 Y，色度从源像素重新计算。</summary>
internal sealed class LumaPlane
{
    private readonly double[] _values;

    public LumaPlane(ImageSize size, double[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length != size.PixelCount)
        {
            throw new ArgumentException("亮度平面长度与图片尺寸不一致。", nameof(values));
        }

        Size = size;
        _values = values;
    }

    public ImageSize Size { get; }

    public double this[int x, int y]
    {
        get => _values[checked((y * Size.Width) + x)];
        set => _values[checked((y * Size.Width) + x)] = value;
    }
}
