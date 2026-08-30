using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using MyAvaloniaManagement.PluginSdk.UI;

namespace ImageLabPlugin.Standalone;

/// <summary>Standalone 使用自己的主窗口提供真实文件选择，不把 Window 传入插件业务层。</summary>
internal sealed class StandaloneWindowInteraction : IPluginWindowInteraction
{
    public async Task<IReadOnlyList<string>> PickOpenFilesAsync(
        FilePickerOpenOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        var window = GetMainWindow();
        if (window?.StorageProvider is null)
        {
            return [];
        }

        var files = await window.StorageProvider.OpenFilePickerAsync(options);
        cancellationToken.ThrowIfCancellationRequested();
        return files.Select(file => file.TryGetLocalPath()).Where(path => path is not null).Cast<string>().ToArray();
    }

    public async Task<string?> PickSaveFileAsync(
        FilePickerSaveOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        var window = GetMainWindow();
        if (window?.StorageProvider is null)
        {
            return null;
        }

        var file = await window.StorageProvider.SaveFilePickerAsync(options);
        cancellationToken.ThrowIfCancellationRequested();
        return file?.TryGetLocalPath();
    }

    public Task<bool> TrySetClipboardTextAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }

    private static Avalonia.Controls.Window? GetMainWindow() =>
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
