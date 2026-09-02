using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Domain.MagnitudePhaseSwap;

/// <summary>描述源图在方形规范画布中的实际内容矩形。</summary>
internal readonly record struct FrequencyPairContentRectangle(int X, int Y, int Width, int Height)
{
    public double CoverageRatio(int canvasSize) => (double)checked(Width * Height) / checked(canvasSize * canvasSize);
}

/// <summary>拥有一张固定尺寸、白底、BT.601 亮度规范画布。</summary>
/// <remarks>
/// 画布是 A/B 逐频点交换的坐标契约。构造时复制 double 缓冲，调用方无法在 FFT 之后修改输入；
/// 预览也由同一缓冲量化得到，避免 UI 另走一套缩放算法而与数值事实不一致。
/// </remarks>
internal sealed class FrequencyPairCanvas
{
    private readonly double[] _values;

    public FrequencyPairCanvas(int size, FrequencyPairContentRectangle content, ReadOnlySpan<double> values,
        string fingerprint)
    {
        MagnitudePhaseCanvasSize.Validate(size);
        if (values.Length != checked(size * size)) throw new ArgumentException("规范画布长度与尺寸不一致。", nameof(values));
        if (content.X < 0 || content.Y < 0 || content.Width <= 0 || content.Height <= 0 ||
            content.X + content.Width > size || content.Y + content.Height > size)
            throw new ArgumentOutOfRangeException(nameof(content), "内容矩形必须完整位于规范画布内。");
        if (string.IsNullOrWhiteSpace(fingerprint) || fingerprint.Length != 24 || !fingerprint.All(Uri.IsHexDigit))
            throw new ArgumentException("内容指纹必须是 24 位十六进制字符串。", nameof(fingerprint));
        foreach (var value in values)
            if (!double.IsFinite(value) || value is < 0d or > 255d)
                throw new ArgumentOutOfRangeException(nameof(values), "规范亮度必须为 [0,255] 内的有限值。");
        Size = size;
        Content = content;
        Fingerprint = fingerprint.ToLowerInvariant();
        _values = values.ToArray();
    }

    public int Size { get; }
    public FrequencyPairContentRectangle Content { get; }
    public string Fingerprint { get; }
    public ReadOnlyMemory<double> Values => _values;

    public PixelImage CreatePreview()
    {
        var rgba = new byte[checked(_values.Length * 4)];
        for (var i = 0; i < _values.Length; i++)
        {
            var level = (byte)Math.Clamp((int)Math.Round(_values[i], MidpointRounding.ToEven), 0, 255);
            rgba[i * 4] = rgba[(i * 4) + 1] = rgba[(i * 4) + 2] = level;
            rgba[(i * 4) + 3] = 255;
        }
        return new PixelImage(new ImageSize(Size, Size), rgba);
    }
}

internal static class MagnitudePhaseCanvasSize
{
    public static void Validate(int size)
    {
        if (size is not (256 or 512 or 1024))
            throw new ArgumentOutOfRangeException(nameof(size), "规范画布只允许 256、512 或 1024。");
    }
}
