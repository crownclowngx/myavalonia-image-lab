using Avalonia.Platform.Storage;
using ImageLabPlugin.Application.Ports;
using MyAvaloniaManagement.PluginSdk.UI;

namespace ImageLabPlugin.Infrastructure.Ui;

/// <summary>把 SDK 文件窗口端口适配成 ImageLab 的四个明确用户意图。</summary>
internal sealed class AvaloniaImageLabFileDialog(IPluginWindowInteraction interaction) : IImageLabFileDialog
{
    private static readonly FilePickerFileType Images = new("图片")
    {
        Patterns = ["*.png", "*.jpg", "*.jpeg"]
    };

    public async Task<string?> PickImageAsync(CancellationToken cancellationToken)
    {
        var paths = await interaction.PickOpenFilesAsync(
            new FilePickerOpenOptions
            {
                Title = "选择 PNG 或 JPEG 图片",
                AllowMultiple = false,
                FileTypeFilter = [Images]
            },
            cancellationToken).ConfigureAwait(false);
        return paths.Count == 0 ? null : paths[0];
    }

    public async Task<string?> PickPayloadAsync(CancellationToken cancellationToken)
    {
        var paths = await interaction.PickOpenFilesAsync(
            new FilePickerOpenOptions { Title = "选择要嵌入的 Payload 文件", AllowMultiple = false },
            cancellationToken).ConfigureAwait(false);
        return paths.Count == 0 ? null : paths[0];
    }

    public Task<string?> PickOutputImageAsync(string suggestedName, CancellationToken cancellationToken) =>
        interaction.PickSaveFileAsync(
            new FilePickerSaveOptions
            {
                Title = "保存水印图片",
                SuggestedFileName = suggestedName,
                FileTypeChoices = [Images]
            },
            cancellationToken);

    public Task<string?> PickPayloadExportAsync(string suggestedName, CancellationToken cancellationToken) =>
        interaction.PickSaveFileAsync(
            new FilePickerSaveOptions
            {
                Title = "导出恢复的 Payload",
                SuggestedFileName = suggestedName
            },
            cancellationToken);
}
