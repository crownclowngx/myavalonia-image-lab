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
}
