using MyAvaloniaManagement.PluginSdk;
namespace ImageLabPlugin.Constants;

public static class PluginIds
{
    public static readonly PluginId Plugin = new("myavalonia.plugin.image.lab");

    /// <summary>“水印写入”工作上下文的持久身份。</summary>
    public static readonly DocumentTypeId WatermarkEmbedDocument =
        new("myavalonia.plugin.image.lab.document.watermark.embed");

    /// <summary>“提取与验证”工作上下文的持久身份。</summary>
    public static readonly DocumentTypeId WatermarkInspectDocument =
        new("myavalonia.plugin.image.lab.document.watermark.inspect");

    /// <summary>“频域分析器”多实例工作上下文的持久身份。</summary>
    public static readonly DocumentTypeId SpectrumInspectorDocument =
        new("myavalonia.plugin.image.lab.document.spectrum-inspector");

    /// <summary>“图像比较实验室”多实例工作上下文的持久身份。</summary>
    public static readonly DocumentTypeId ImageCompareLabDocument =
        new("myavalonia.plugin.image.lab.document.image-compare-lab");

    /// <summary>“鲁棒性实验室”多实例受控实验工作上下文的持久身份。</summary>
    public static readonly DocumentTypeId RobustnessLabDocument =
        new("myavalonia.plugin.image.lab.document.robustness-lab");

    /// <summary>“感知指纹”双图比较与稳定性试验的多实例持久身份。</summary>
    public static readonly DocumentTypeId ImageFingerprintDocument =
        new("myavalonia.plugin.image.lab.document.image-fingerprint");

    /// <summary>“位平面观察器”单图拆位与掩码重建的多实例持久身份。</summary>
    public static readonly DocumentTypeId BitPlaneViewerDocument =
        new("myavalonia.plugin.image.lab.document.bit-plane-viewer");

    /// <summary>“LSB 隐写与统计实验”多实例教学工作上下文的持久身份。</summary>
    public static readonly DocumentTypeId LsbSteganographyLabDocument =
        new("myavalonia.plugin.image.lab.document.lsb-steganography-lab");

    /// <summary>“卷积核实验台”多实例空间卷积教学工作上下文的持久身份。</summary>
    public static readonly DocumentTypeId ConvolutionPlaygroundDocument =
        new("myavalonia.plugin.image.lab.document.convolution-playground");

    /// <summary>“小波实验室”多尺度分解、去噪与载体比较的多实例持久身份。</summary>
    public static readonly DocumentTypeId WaveletLabDocument =
        new("myavalonia.plugin.image.lab.document.wavelet-lab");

    /// <summary>“频域滤波”径向滤波、副作用诊断与空间近似的多实例持久身份。</summary>
    public static readonly DocumentTypeId FrequencyFilterDocument =
        new("myavalonia.plugin.image.lab.document.frequency-filter");

    /// <summary>“频谱遮罩编辑器”多实例绘制、重建与配方实验的持久身份。</summary>
    public static readonly DocumentTypeId FrequencyMaskEditorDocument =
        new("myavalonia.plugin.image.lab.document.frequency-mask-editor");

    /// <summary>“周期噪声与陷波器”多实例候选复核与共轭安全重建的持久身份。</summary>
    public static readonly DocumentTypeId PeriodicNoiseRemovalDocument =
        new("myavalonia.plugin.image.lab.document.periodic-noise-removal");

    /// <summary>“奇异值分解重建”低秩分析与策略比较的多实例持久身份。</summary>
    public static readonly DocumentTypeId SvdDecompositionDocument =
        new("myavalonia.plugin.image.lab.document.svd-decomposition");

    /// <summary>“调色板与颜色迁移”多实例颜色统计实验的持久身份。</summary>
    public static readonly DocumentTypeId PaletteColorTransferDocument =
        new("myavalonia.plugin.image.lab.document.palette-color-transfer");

    /// <summary>“内容感知缩放”多实例 Seam Carving 实验的持久身份。</summary>
    public static readonly DocumentTypeId SeamCarvingDocument =
        new("myavalonia.plugin.image.lab.document.seam-carving");
}
