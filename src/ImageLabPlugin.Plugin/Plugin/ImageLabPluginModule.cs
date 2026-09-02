using MyAvaloniaManagement.PluginSdk.UI;
using ImageLabPlugin.Constants;
using ImageLabPlugin.Features.WatermarkEmbed;
using ImageLabPlugin.Features.WatermarkInspect;
using ImageLabPlugin.Features.SpectrumInspector;
using ImageLabPlugin.Features.ImageCompareLab;
using ImageLabPlugin.Features.RobustnessLab;
using ImageLabPlugin.Features.ImageFingerprint;
using ImageLabPlugin.Features.BitPlaneViewer;
using ImageLabPlugin.Features.LsbSteganographyLab;
using ImageLabPlugin.Features.ConvolutionPlayground;
using ImageLabPlugin.Features.WaveletLab;
using ImageLabPlugin.Features.FrequencyFilter;
using ImageLabPlugin.Features.FrequencyMaskEditor;
using ImageLabPlugin.Features.PeriodicNoiseRemoval;
using ImageLabPlugin.Features.SvdDecomposition;
using ImageLabPlugin.Features.PaletteColorTransfer;
using ImageLabPlugin.Features.SeamCarving;
using ImageLabPlugin.Features.PoissonBlending;
using ImageLabPlugin.Features.SpectralArt;
using ImageLabPlugin.Features.HybridImage;
using ImageLabPlugin.Features.MagnitudePhaseSwap;
using ImageLabPlugin.Features.ImageOscilloscope;

namespace ImageLabPlugin.Plugin;

public sealed class ImageLabPluginModule : IPluginModule
{
    public void Configure(IPluginRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        registration.Services.AddImageLabPluginServices();
        registration.AddPersistableDocument<WatermarkEmbedDocument, WatermarkEmbedView>(
            new DocumentDescriptor(
                PluginIds.WatermarkEmbedDocument,
                "水印写入",
                "把文本、JSON 或文件 Payload 写入图片并生成经过回读验证的新图片",
                "图像安全"));
        registration.AddPersistableDocument<WatermarkInspectDocument, WatermarkInspectView>(
            new DocumentDescriptor(
                PluginIds.WatermarkInspectDocument,
                "提取与验证",
                "检测、恢复并验证 ImageLab 频域隐式水印",
                "图像安全"));
        registration.AddPersistableDocument<SpectrumInspectorDocument, SpectrumInspectorView>(
            new DocumentDescriptor(
                PluginIds.SpectrumInspectorDocument,
                "频域分析器",
                "观察图像通道的全局 FFT、分块 DCT、频带能量与逆变换结果",
                "图像分析"));
        registration.AddPersistableDocument<ImageCompareLabDocument, ImageCompareLabView>(
            new DocumentDescriptor(
                PluginIds.ImageCompareLabDocument,
                "图像比较实验室",
                "以同步视图、像素差异、客观指标和直方图比较两张同尺寸图片",
                "图像分析"));
        registration.AddPersistableDocument<RobustnessLabDocument, RobustnessLabView>(
            new DocumentDescriptor(
                PluginIds.RobustnessLabDocument,
                "鲁棒性实验室",
                "以可复现扰动链、参数扫描和分步诊断测量 ImageLab 水印的恢复边界",
                "图像安全"));
        registration.AddPersistableDocument<ImageFingerprintDocument, ImageFingerprintView>(
            new DocumentDescriptor(
                PluginIds.ImageFingerprintDocument,
                "感知指纹",
                "使用 aHash、dHash 和 pHash 比较两张显式图片的感知相似性与稳定性",
                "图像分析"));
        registration.AddPersistableDocument<BitPlaneViewerDocument, BitPlaneViewerView>(
            new DocumentDescriptor(
                PluginIds.BitPlaneViewerDocument,
                "位平面观察器",
                "拆分 R、G、B、Alpha 或 Y 的 8 个位平面并观察掩码重建结果",
                "图像分析"));
        registration.AddPersistableDocument<LsbSteganographyLabDocument, LsbSteganographyLabView>(
            new DocumentDescriptor(
                PluginIds.LsbSteganographyLabDocument,
                "LSB 隐写与统计实验",
                "以像素低位写入、位置可视化、统计对比和受控扰动观察 LSB 隐写的可检测性与脆弱性",
                "图像安全"));
        registration.AddPersistableDocument<ConvolutionPlaygroundDocument, ConvolutionPlaygroundView>(
            new DocumentDescriptor(
                PluginIds.ConvolutionPlaygroundDocument,
                "卷积核实验台",
                "编辑空间卷积核，并联动观察边界、差异、像素贡献和频率响应",
                "图像分析"));
        registration.AddPersistableDocument<WaveletLabDocument, WaveletLabView>(
            new DocumentDescriptor(
                PluginIds.WaveletLabDocument,
                "小波实验室",
                "分解、观察并重建多尺度小波子带，实验阈值去噪及 DCT/DWT 水印差异",
                "图像分析"));
        registration.AddPersistableDocument<FrequencyFilterDocument, FrequencyFilterView>(
            new DocumentDescriptor(
                PluginIds.FrequencyFilterDocument,
                "频域滤波",
                "使用 Ideal、Butterworth 或 Gaussian 径向响应实验低通、高通、带通、带阻及空间有限核近似",
                "图像分析"));
        registration.AddPersistableDocument<FrequencyMaskEditorDocument, FrequencyMaskEditorView>(
            new DocumentDescriptor(
                PluginIds.FrequencyMaskEditorDocument,
                "频谱遮罩编辑器",
                "在中心化频谱上绘制共轭安全的实数增益遮罩，并联动观察空间域重建与诊断",
                "图像分析"));
        registration.AddPersistableDocument<PeriodicNoiseRemovalDocument, PeriodicNoiseRemovalView>(
            new DocumentDescriptor(
                PluginIds.PeriodicNoiseRemovalDocument,
                "周期噪声与陷波器",
                "复核周期频率候选，以必须人工采用的共轭安全陷波草案观察频谱、重建、差异与不可逆损失",
                "图像分析"));
        registration.AddPersistableDocument<SvdDecompositionDocument, SvdDecompositionView>(
            new DocumentDescriptor(
                PluginIds.SvdDecompositionDocument,
                "奇异值分解重建",
                "以有界分析代理观察奇异值、Rank-k 重建、秩一分量和固定颜色策略差异",
                "图像分析"));
        registration.AddPersistableDocument<PaletteColorTransferDocument, PaletteColorTransferView>(
            new DocumentDescriptor(
                PluginIds.PaletteColorTransferDocument,
                "调色板与颜色迁移",
                "观察 Alpha 加权颜色分布、确定性主色、CIELAB 统计迁移和固定调色板量化误差",
                "图像分析"));
        registration.AddPersistableDocument<SeamCarvingDocument, SeamCarvingView>(
            new DocumentDescriptor(
                PluginIds.SeamCarvingDocument,
                "内容感知缩放",
                "以 Sobel 能量、显式区域偏置和逐缝播放观察 Seam Carving，并与普通缩放比较",
                "图像分析"));
        registration.AddPersistableDocument<PoissonBlendingDocument, PoissonBlendingView>(
            new DocumentDescriptor(
                PluginIds.PoissonBlendingDocument,
                "梯度域融合",
                "以二值遮罩、整数平移和确定性 Poisson 迭代比较梯度域融合与直接 Alpha 合成",
                "图像分析"));
        // 只在既有十七个稳定身份之后追加；不得改变旧 ID、顺序或 Tool 数量。
        registration.AddPersistableDocument<SpectralArtDocument, SpectralArtView>(
            new DocumentDescriptor(
                PluginIds.SpectralArtDocument,
                "频谱艺术",
                "把文字、Logo 或二维码图片映射为共轭安全的 FFT 幅度图案，并观察空间质量与频域诊断",
                "图像分析"));
        // Hybrid Image 只在既有十八个稳定身份之后追加；旧 ID、顺序和零 Tool 约束保持不变。
        registration.AddPersistableDocument<HybridImageDocument, HybridImageView>(
            new DocumentDescriptor(
                PluginIds.HybridImageDocument,
                "混合图像",
                "以控制点对齐、Gaussian 低高频和真实多尺度预览实验近看／远看主体切换",
                "图像分析"));
        // 幅相交换只在既有十九个稳定身份之后追加；旧 ID、顺序和零 Tool 约束保持不变。
        registration.AddPersistableDocument<MagnitudePhaseSwapDocument, MagnitudePhaseSwapView>(
            new DocumentDescriptor(
                PluginIds.MagnitudePhaseSwapDocument,
                "幅度与相位交换",
                "交换或插值两张图片 FFT 的幅度与相位并联动观察重建",
                "图像分析"));
        // 图像示波器只在既有二十个稳定身份之后追加；旧 ID、顺序和零 Tool 约束保持不变。
        registration.AddPersistableDocument<ImageOscilloscopeDocument, ImageOscilloscopeView>(
            new DocumentDescriptor(
                PluginIds.ImageOscilloscopeDocument,
                "图像示波器",
                "用全图 Waveform、RGB Parade、Vectorscope、直方图与探针观察静态图片信号",
                "图像分析"));
    }
}
