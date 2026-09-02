namespace ImageLabPlugin.Domain.Shared.Imaging;

/// <summary>生成受控最大边的抗混叠分析代理。</summary>
/// <remarks>
/// 缩小时每个目标像素按其覆盖的源像素面积加权平均。该实现比最近邻慢一些，但不会把高频纹理折叠成
/// 虚假低频峰；小图不放大并直接克隆，使全通重建可以保持逐字节一致。
/// </remarks>
internal sealed class ImageAnalysisProxyProjector(ImageAreaResampler resampler)
{
    public static readonly int[] SupportedMaximumEdges = [512, 1024, 2048];

    /// <summary>保留既有直接构造入口；组合根会优先使用带共享 resampler 的构造函数。</summary>
    public ImageAnalysisProxyProjector() : this(new ImageAreaResampler())
    {
    }

    public PixelImage Create(PixelImage source, int maximumEdge, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!SupportedMaximumEdges.Contains(maximumEdge))
        {
            throw new ArgumentOutOfRangeException(nameof(maximumEdge), maximumEdge, "分析档位只能是 512、1024 或 2048。 ");
        }

        return resampler.ResizeToMaximumEdge(source, maximumEdge, cancellationToken);
    }
}
