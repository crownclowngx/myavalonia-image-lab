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
    }
}
