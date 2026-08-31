namespace ImageLabPlugin.Domain.Imaging;

/// <summary>非预乘、归一化到 [0,1] 的 sRGB 编码颜色。</summary>
/// <remarks>Alpha 不属于颜色空间；调用方按产品协议单独保存和统计 Alpha。</remarks>
internal readonly record struct SrgbColor
{
    public SrgbColor(double red, double green, double blue)
    {
        ValidateChannel(red, nameof(red));
        ValidateChannel(green, nameof(green));
        ValidateChannel(blue, nameof(blue));
        Red = red;
        Green = green;
        Blue = blue;
    }

    public double Red { get; }
    public double Green { get; }
    public double Blue { get; }

    public static SrgbColor FromBytes(byte red, byte green, byte blue) =>
        new(red / 255d, green / 255d, blue / 255d);

    public (byte Red, byte Green, byte Blue) ToBytes() =>
        (ToByte(Red), ToByte(Green), ToByte(Blue));

    private static byte ToByte(double value) =>
        (byte)Math.Round(value * 255d, MidpointRounding.ToEven);

    private static void ValidateChannel(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0d or > 1d)
            throw new ArgumentOutOfRangeException(name, value, "sRGB 编码通道必须是 [0,1] 内的有限数。");
    }
}

/// <summary>尚未编码传递函数的线性 RGB；色域判断前允许暂时超出 [0,1]。</summary>
internal readonly record struct LinearRgbColor(double Red, double Green, double Blue)
{
    public bool IsFinite => double.IsFinite(Red) && double.IsFinite(Green) && double.IsFinite(Blue);
    public bool IsInGamut(double tolerance = 1e-12) => IsFinite &&
        Red >= -tolerance && Red <= 1d + tolerance &&
        Green >= -tolerance && Green <= 1d + tolerance &&
        Blue >= -tolerance && Blue <= 1d + tolerance;
}

/// <summary>以 Y=1 为白色尺度的 XYZ D65 三刺激值。</summary>
internal readonly record struct XyzD65Color(double X, double Y, double Z)
{
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);
}

/// <summary>
/// 实现 IEC sRGB 分段传递函数和标准 D65 矩阵；本服务无状态且不持有图片。
/// </summary>
internal sealed class SrgbColorSpace
{
    /// <summary>V1 颜色协议稳定标识；报告和 fingerprint 必须保存它。</summary>
    public const string ProtocolId = "srgb-d65-cielab-v1";

    /// <summary>
    /// 把 sRGB 编码值解码为线性光。0.04045 是编码侧分段阈值，不能把字节值直接乘 XYZ 矩阵。
    /// </summary>
    public LinearRgbColor Decode(SrgbColor color) => new(
        DecodeChannel(color.Red), DecodeChannel(color.Green), DecodeChannel(color.Blue));

    /// <summary>把色域内线性 RGB 编码成 sRGB；仅吸收矩阵往返产生的 1e-12 级舍入误差。</summary>
    public SrgbColor Encode(LinearRgbColor color)
    {
        if (!color.IsFinite) throw new ArgumentException("线性 RGB 不能包含 NaN 或 Infinity。", nameof(color));
        // 标准正反矩阵只给到 7 位小数，往返边界原色可累积约 1e-7 误差；1e-6 只吸收该误差，
        // 真实超色域颜色仍必须经过 SrgbGamutMapper，不能借此隐藏逐通道裁切。
        if (!color.IsInGamut(1e-6)) throw new ArgumentOutOfRangeException(nameof(color), "线性 RGB 位于 sRGB 色域外，必须先经过显式色域映射。");
        return new SrgbColor(
            EncodeChannel(Math.Clamp(color.Red, 0d, 1d)),
            EncodeChannel(Math.Clamp(color.Green, 0d, 1d)),
            EncodeChannel(Math.Clamp(color.Blue, 0d, 1d)));
    }

    /// <summary>线性 sRGB 到 XYZ D65；矩阵常量按 Y=1 白色尺度冻结。</summary>
    public XyzD65Color ToXyz(LinearRgbColor color)
    {
        if (!color.IsFinite) throw new ArgumentException("线性 RGB 不能包含非有限数。", nameof(color));
        return new XyzD65Color(
            (0.4124564 * color.Red) + (0.3575761 * color.Green) + (0.1804375 * color.Blue),
            (0.2126729 * color.Red) + (0.7151522 * color.Green) + (0.0721750 * color.Blue),
            (0.0193339 * color.Red) + (0.1191920 * color.Green) + (0.9503041 * color.Blue));
    }

    /// <summary>XYZ D65 到线性 sRGB；返回值允许超色域，不能在这里偷偷逐通道裁切。</summary>
    public LinearRgbColor FromXyz(XyzD65Color color)
    {
        if (!color.IsFinite) throw new ArgumentException("XYZ 不能包含非有限数。", nameof(color));
        return new LinearRgbColor(
            (3.2404542 * color.X) - (1.5371385 * color.Y) - (0.4985314 * color.Z),
            (-0.9692660 * color.X) + (1.8760108 * color.Y) + (0.0415560 * color.Z),
            (0.0556434 * color.X) - (0.2040259 * color.Y) + (1.0572252 * color.Z));
    }

    internal static double DecodeChannel(double channel) => channel <= 0.04045d
        ? channel / 12.92d
        : Math.Pow((channel + 0.055d) / 1.055d, 2.4d);

    internal static double EncodeChannel(double channel) => channel <= 0.0031308d
        ? 12.92d * channel
        : (1.055d * Math.Pow(channel, 1d / 2.4d)) - 0.055d;
}
