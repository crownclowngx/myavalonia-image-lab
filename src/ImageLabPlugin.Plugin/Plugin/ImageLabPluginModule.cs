using MyAvaloniaManagement.PluginSdk.UI;
using ImageLabPlugin.Constants;
using ImageLabPlugin.Features.WatermarkEmbed;
using ImageLabPlugin.Features.WatermarkInspect;
using ImageLabPlugin.Features.SpectrumInspector;
using ImageLabPlugin.Features.ImageCompareLab;
using ImageLabPlugin.Features.RobustnessLab;

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
    }
}
