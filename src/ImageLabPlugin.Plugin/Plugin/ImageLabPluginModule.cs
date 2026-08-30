using MyAvaloniaManagement.PluginSdk.UI;
using ImageLabPlugin.Constants;
using ImageLabPlugin.Features.WatermarkEmbed;
using ImageLabPlugin.Features.WatermarkInspect;

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
    }
}
