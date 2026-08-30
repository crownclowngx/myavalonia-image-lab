namespace ImageLabPlugin.Domain.Frequency;

internal readonly record struct FrequencyPoint(
    int DisplayX,
    int DisplayY,
    int InternalX,
    int InternalY,
    int Kx,
    int Ky,
    double Fx,
    double Fy,
    double Radius);

/// <summary>统一 FFT 自然索引、中心化显示坐标和归一化径向频率。</summary>
/// <remarks>
/// 半径把 Nyquist 方形的四角归一化到 1：<c>ρ = sqrt((fx/.5)^2+(fy/.5)^2)/sqrt(2)</c>。
/// UI、能量报表和遮罩共享此类，避免相同像素在不同面板被划入不同频带。
/// </remarks>
internal static class FrequencyCoordinates
{
    public static FrequencyPoint FromDisplay(int displayX, int displayY, int width, int height)
    {
        Validate(displayX, displayY, width, height);
        var internalX = (displayX + (width / 2)) % width;
        var internalY = (displayY + (height / 2)) % height;
        var kx = displayX - (width / 2);
        var ky = displayY - (height / 2);
        var fx = kx / (double)width;
        var fy = ky / (double)height;
        var radius = Math.Clamp(Math.Sqrt(Math.Pow(fx / 0.5d, 2) + Math.Pow(fy / 0.5d, 2)) / Math.Sqrt(2d), 0d, 1d);
        return new FrequencyPoint(displayX, displayY, internalX, internalY, kx, ky, fx, fy, radius);
    }

    public static FrequencyPoint FromInternal(int internalX, int internalY, int width, int height)
    {
        Validate(internalX, internalY, width, height);
        var displayX = (internalX + (width / 2)) % width;
        var displayY = (internalY + (height / 2)) % height;
        return FromDisplay(displayX, displayY, width, height);
    }

    public static (int X, int Y) ConjugateIndex(int internalX, int internalY, int width, int height)
    {
        Validate(internalX, internalY, width, height);
        return ((width - internalX) % width, (height - internalY) % height);
    }

    private static void Validate(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0 || (uint)x >= (uint)width || (uint)y >= (uint)height)
        {
            throw new ArgumentOutOfRangeException(nameof(x), $"频率坐标 ({x},{y}) 超出 {width}×{height}。 ");
        }
    }
}
