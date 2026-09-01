using System.Numerics;
using ImageLabPlugin.Domain.Frequency;
using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Domain.SpectralArt;

internal sealed record SpectralRawStatistics(
    double Minimum,
    double Maximum,
    long BelowZero,
    long Above255,
    int ClippedPixels,
    int ClippedRgbComponents,
    double MaximumImaginaryResidual);

internal sealed record SpectralArtReconstruction(
    PixelImage Image,
    ImageChannelPlane LumaPlane,
    SpectralRawStatistics RawStatistics);

/// <summary>消费已写入的唯一工作频谱，执行共享 IFFT、crop 和 Y 通道回写。</summary>
/// <remarks>
/// ownedWorkingSpectrum 在调用后已经被 IFFT 原地改写，调用方不得再次把它当成频谱使用；因此所有频谱预览和
/// 可见性诊断必须在进入本服务前完成。共享逆变换器负责有限值和 1E-8 虚部门禁，本服务只负责把裁回的 raw Y
/// 交给现有 ImageChannelConverter，从而沿用项目统一的颜色关系、ToEven 量化、Alpha 保持和裁切统计。
/// </remarks>
internal sealed class SpectralArtReconstructor(
    FrequencyInverseTransformer inverseTransformer,
    ImageChannelConverter channelConverter)
{
    public SpectralArtReconstruction Reconstruct(
        PixelImage sourceImage,
        FrequencySpectrum sourceSpectrum,
        Complex[] ownedWorkingSpectrum,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceImage);
        ArgumentNullException.ThrowIfNull(sourceSpectrum);
        ArgumentNullException.ThrowIfNull(ownedWorkingSpectrum);
        if (sourceImage.Size != sourceSpectrum.SourceSize)
            throw new ArgumentException("源图尺寸与频谱源尺寸不一致。", nameof(sourceImage));
        var padded = inverseTransformer.InverseOwned(ownedWorkingSpectrum,
            sourceSpectrum.PaddedWidth, sourceSpectrum.PaddedHeight, cancellationToken);
        var cropped = inverseTransformer.Crop(padded, sourceSpectrum.SourceSize, cancellationToken);
        double minimum = double.PositiveInfinity, maximum = double.NegativeInfinity;
        long below = 0, above = 0;
        for (var i = 0; i < cropped.Length; i++)
        {
            if ((i & 16383) == 0) cancellationToken.ThrowIfCancellationRequested();
            minimum = Math.Min(minimum, cropped[i]);
            maximum = Math.Max(maximum, cropped[i]);
            if (cropped[i] < 0d) below++;
            else if (cropped[i] > 255d) above++;
        }
        var plane = new ImageChannelPlane(sourceImage.Size, ImageChannel.Luma, cropped);
        var reconstructed = channelConverter.Apply(sourceImage, plane, MidpointRounding.ToEven);
        return new SpectralArtReconstruction(reconstructed.Image, plane,
            new SpectralRawStatistics(minimum, maximum, below, above,
                reconstructed.ClippedPixelCount, reconstructed.ClippedComponentCount,
                padded.MaximumImaginaryResidual));
    }
}
