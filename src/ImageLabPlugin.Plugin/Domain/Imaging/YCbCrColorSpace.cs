namespace ImageLabPlugin.Domain.Imaging;

/// <summary>集中定义 ImageLab 使用的 BT.601 全范围 YCbCr 单像素换算。</summary>
/// <remarks>
/// 该原语只处理数值，不拥有图片缓冲区。频域、通道重建和位平面观察器共同调用它，避免三处公式
/// 随时间产生不同的舍入或系数。Y 的 byte 量化显式采用银行家舍入（中点取偶），Alpha 不属于
/// 颜色空间，因此由调用方原样保存。
/// </remarks>
internal static class YCbCrColorSpace
{
    public static YCbCrSample FromRgb(byte red, byte green, byte blue) => new(
        (0.299d * red) + (0.587d * green) + (0.114d * blue),
        128d - (0.168736d * red) - (0.331264d * green) + (0.5d * blue),
        128d + (0.5d * red) - (0.418688d * green) - (0.081312d * blue));

    public static RgbSample ToRgb(double luma, double chromaBlue, double chromaRed) => new(
        luma + (1.402d * (chromaRed - 128d)),
        luma - (0.344136d * (chromaBlue - 128d)) - (0.714136d * (chromaRed - 128d)),
        luma + (1.772d * (chromaBlue - 128d)));

    /// <summary>把连续 Y 量化为位运算所需的 8 位样本。</summary>
    public static byte QuantizeLuma(byte red, byte green, byte blue) =>
        ClampToByte(Math.Round(FromRgb(red, green, blue).Luma, MidpointRounding.ToEven), out _);

    public static byte ClampToByte(double value, out bool clipped)
    {
        clipped = value < 0d || value > 255d;
        return (byte)Math.Clamp((int)Math.Round(value, MidpointRounding.ToEven), 0, 255);
    }
}

/// <summary>未量化的 BT.601 全范围 YCbCr 分量。</summary>
internal readonly record struct YCbCrSample(double Luma, double ChromaBlue, double ChromaRed);

/// <summary>逆变换后、尚未裁切到 byte 的 RGB 分量。</summary>
internal readonly record struct RgbSample(double Red, double Green, double Blue);
