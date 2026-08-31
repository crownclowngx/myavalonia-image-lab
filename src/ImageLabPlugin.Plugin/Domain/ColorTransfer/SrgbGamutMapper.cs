using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Domain.ColorTransfer;

/// <summary>将可能超出 sRGB 色域的 Lab 颜色确定性映射为可编码颜色。</summary>
/// <remarks>
/// V1 保留裁切后的 L* 与色相，只沿 a*/b* 色度射线二分 24 次。逐通道 RGB clamp 会不透明地偏移色相，
/// 因此这里只允许最后吸收矩阵舍入误差；映射距离留给上层聚合并明确展示。
/// </remarks>
internal sealed class SrgbGamutMapper(SrgbColorSpace srgb, CieLabColorSpace lab, CieDeltaE deltaE)
{
    public const int BisectionIterations = 24;

    public GamutMappedColor Map(CieLabColor original)
    {
        if (!original.IsFinite) throw new ArgumentException("待映射 Lab 不能包含非有限数。", nameof(original));
        var clippedL = Math.Clamp(original.L, 0d, 100d);
        var working = original with { L = clippedL };
        var linear = srgb.FromXyz(lab.FromLab(working));
        if (linear.IsInGamut())
        {
            var encoded = srgb.Encode(linear);
            var kind = clippedL == original.L ? GamutMappingKind.None : GamutMappingKind.LightnessClipped;
            return new GamutMappedColor(encoded, working, kind, deltaE.DeltaE76(original, working));
        }

        var low = 0d; var high = 1d;
        for (var iteration = 0; iteration < BisectionIterations; iteration++)
        {
            var middle = (low + high) / 2d;
            var candidate = new CieLabColor(clippedL, original.A * middle, original.B * middle);
            if (srgb.FromXyz(lab.FromLab(candidate)).IsInGamut()) low = middle; else high = middle;
        }
        var mapped = new CieLabColor(clippedL, original.A * low, original.B * low);
        var mappedLinear = srgb.FromXyz(lab.FromLab(mapped));
        var mappedSrgb = srgb.Encode(mappedLinear);
        var mappingKind = GamutMappingKind.ChromaCompressed;
        if (clippedL != original.L) mappingKind |= GamutMappingKind.LightnessClipped;
        return new GamutMappedColor(mappedSrgb, mapped, mappingKind, deltaE.DeltaE76(original, mapped));
    }
}
