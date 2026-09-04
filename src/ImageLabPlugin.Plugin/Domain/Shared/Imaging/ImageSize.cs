namespace ImageLabPlugin.Domain.Shared.Imaging;

/// <summary>表示经过验证的像素尺寸，并集中保护像素数和内存预算。</summary>
internal readonly record struct ImageSize
{
    /// <summary>V1 允许解码的最大像素数。该上限在分配大缓冲区前拒绝异常输入。</summary>
    public const long MaximumPixelCount = 16_777_216;

    public ImageSize(int width, int height)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "图片宽度必须大于零。");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "图片高度必须大于零。");
        }

        var pixelCount = checked((long)width * height);
        if (pixelCount > MaximumPixelCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                $"图片包含 {pixelCount:N0} 个像素，超过 V1 的 {MaximumPixelCount:N0} 像素安全上限。");
        }

        Width = width;
        Height = height;
    }

    public int Width { get; }
    public int Height { get; }
    public long PixelCount => (long)Width * Height;
}
