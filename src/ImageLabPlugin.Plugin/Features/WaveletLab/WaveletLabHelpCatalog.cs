namespace ImageLabPlugin.Features.WaveletLab;

/// <summary>集中维护界面教学边界，避免多个状态分支给出互相矛盾的结论。</summary>
internal static class WaveletLabHelpCatalog
{
    public const string Summary =
        "LH 表示 X 低通/Y 高通，HL 表示 X 高通/Y 低通。系数响应、PSNR/SSIM 与单组水印实验都只描述当前离散基和实验条件，不能推导物体语义或普遍优劣。";
}
