using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Domain.Comparison;

internal enum ImagePairMismatchReason
{
    SizeMismatch
}

/// <summary>描述两张图片不能建立逐像素对应关系的结构化原因。</summary>
internal sealed record ImagePairMismatch(
    ImagePairMismatchReason Reason,
    ImageSize ReferenceSize,
    ImageSize CandidateSize)
{
    public int WidthDifference => CandidateSize.Width - ReferenceSize.Width;
    public int HeightDifference => CandidateSize.Height - ReferenceSize.Height;

    public string ToUserMessage() =>
        $"参考图 {ReferenceSize.Width}×{ReferenceSize.Height}，待比较图 {CandidateSize.Width}×{CandidateSize.Height}，" +
        $"宽度差 {WidthDifference:+#;-#;0}、高度差 {HeightDifference:+#;-#;0}。" +
        "尚未执行对齐或缩放，因此没有生成可比较指标。";
}

/// <summary>集中维护“只有同尺寸图片才可比较”的唯一领域事实。</summary>
internal sealed class ImagePairValidator
{
    public ImagePairMismatch? Validate(PixelImage reference, PixelImage candidate)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(candidate);
        return reference.Size == candidate.Size
            ? null
            : new ImagePairMismatch(ImagePairMismatchReason.SizeMismatch, reference.Size, candidate.Size);
    }

    public void EnsureComparable(PixelImage reference, PixelImage candidate)
    {
        var mismatch = Validate(reference, candidate);
        if (mismatch is not null)
        {
            throw new ArgumentException(mismatch.ToUserMessage(), nameof(candidate));
        }
    }
}

internal readonly record struct ImagePoint(int X, int Y);

internal readonly record struct RgbaPixel(byte R, byte G, byte B, byte A);

/// <summary>原图坐标上的双像素报告；所有有符号变化均固定为 Candidate - Reference。</summary>
internal sealed record ImagePairPixelReport(
    ImagePoint Point,
    RgbaPixel Reference,
    RgbaPixel Candidate,
    double ReferenceLuma,
    double CandidateLuma,
    int DeltaRed,
    int DeltaGreen,
    int DeltaBlue,
    int DeltaAlpha,
    double DeltaLuma)
{
    public int AbsoluteRed => Math.Abs(DeltaRed);
    public int AbsoluteGreen => Math.Abs(DeltaGreen);
    public int AbsoluteBlue => Math.Abs(DeltaBlue);
    public int AbsoluteAlpha => Math.Abs(DeltaAlpha);
    public double AbsoluteLuma => Math.Abs(DeltaLuma);
    public int MaximumRgbDifference => Math.Max(AbsoluteRed, Math.Max(AbsoluteGreen, AbsoluteBlue));
    public bool IsAlphaOnlyChange => MaximumRgbDifference == 0 && AbsoluteAlpha > 0;
}

internal enum HeatmapScalarSource
{
    MaximumRgb,
    Luma
}

internal enum DifferenceProjectionKind
{
    Rgb,
    Heatmap
}

internal sealed record DifferenceProjectionOptions(
    DifferenceProjectionKind Kind,
    int Amplification,
    HeatmapScalarSource HeatmapSource = HeatmapScalarSource.MaximumRgb);

internal sealed record DifferenceProjectionResult(
    PixelImage Image,
    int SaturatedProxyPixelCount,
    DifferenceProjectionOptions Options);
