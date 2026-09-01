using Avalonia.Platform.Storage;
using ImageLabPlugin.Application.Ports;
using MyAvaloniaManagement.PluginSdk.UI;

namespace ImageLabPlugin.Infrastructure.Ui;

/// <summary>把 SDK 文件窗口端口适配成 ImageLab 的四个明确用户意图。</summary>
internal sealed class AvaloniaImageLabFileDialog(IPluginWindowInteraction interaction) :
    IImageFileDialog, IPayloadFileDialog, IComparisonReportFileDialog, IRobustnessReportFileDialog, IFingerprintReportFileDialog, ILsbReportFileDialog, IWaveletReportFileDialog, ISvdFileDialog, IFrequencyMaskRecipeFileDialog, IPeriodicNoiseFileDialog, IColorTransferFileDialog, ISeamCarvingFileDialog, IPoissonBlendingFileDialog, ISpectralArtFileDialog, ITextClipboard
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

    public Task<string?> PickSummaryOutputAsync(string suggestedName, CancellationToken cancellationToken) =>
        interaction.PickSaveFileAsync(
            new FilePickerSaveOptions
            {
                Title = "导出图像比较摘要",
                SuggestedFileName = suggestedName,
                FileTypeChoices = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }]
            },
            cancellationToken);

    public Task<string?> PickJsonOutputAsync(string suggestedName, CancellationToken cancellationToken) =>
        interaction.PickSaveFileAsync(new FilePickerSaveOptions { Title = "导出鲁棒性 JSON 报告", SuggestedFileName = suggestedName, FileTypeChoices = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }] }, cancellationToken);

    public Task<string?> PickCsvOutputAsync(string suggestedName, CancellationToken cancellationToken) =>
        interaction.PickSaveFileAsync(new FilePickerSaveOptions { Title = "导出鲁棒性 CSV 案例表", SuggestedFileName = suggestedName, FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }] }, cancellationToken);

    public Task<string?> PickFingerprintJsonOutputAsync(string suggestedName, CancellationToken cancellationToken) =>
        interaction.PickSaveFileAsync(new FilePickerSaveOptions { Title = "导出感知指纹报告", SuggestedFileName = suggestedName, FileTypeChoices = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }] }, cancellationToken);

    public Task<string?> PickLsbJsonOutputAsync(string suggestedName, CancellationToken cancellationToken) =>
        interaction.PickSaveFileAsync(new FilePickerSaveOptions { Title = "导出 LSB 教学实验 JSON 报告", SuggestedFileName = suggestedName, FileTypeChoices = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }] }, cancellationToken);

    public Task<string?> PickLsbCsvOutputAsync(string suggestedName, CancellationToken cancellationToken) =>
        interaction.PickSaveFileAsync(new FilePickerSaveOptions { Title = "导出 LSB 教学实验 CSV 报告", SuggestedFileName = suggestedName, FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }] }, cancellationToken);

    public Task<string?> PickWaveletJsonOutputAsync(string suggestedName, CancellationToken cancellationToken) =>
        interaction.PickSaveFileAsync(new FilePickerSaveOptions { Title = "导出小波实验 JSON 报告", SuggestedFileName = suggestedName, FileTypeChoices = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }] }, cancellationToken);

    public Task<string?> PickWaveletCsvOutputAsync(string suggestedName, CancellationToken cancellationToken) =>
        interaction.PickSaveFileAsync(new FilePickerSaveOptions { Title = "导出小波实验 CSV 案例表", SuggestedFileName = suggestedName, FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }] }, cancellationToken);

    public Task<string?> PickProxyPngOutputAsync(string suggestedName, CancellationToken cancellationToken) =>
        interaction.PickSaveFileAsync(new FilePickerSaveOptions
        {
            Title = "导出分析代理重建 PNG",
            SuggestedFileName = suggestedName,
            FileTypeChoices = [new FilePickerFileType("PNG") { Patterns = ["*.png"] }]
        }, cancellationToken);

    public Task<string?> PickSvdJsonOutputAsync(string suggestedName, CancellationToken cancellationToken) =>
        interaction.PickSaveFileAsync(new FilePickerSaveOptions
        {
            Title = "导出 SVD 实验 JSON 报告",
            SuggestedFileName = suggestedName,
            FileTypeChoices = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }]
        }, cancellationToken);

    public Task<string?> PickSvdCsvOutputAsync(string suggestedName, CancellationToken cancellationToken) =>
        interaction.PickSaveFileAsync(new FilePickerSaveOptions
        {
            Title = "导出 SVD 实验 CSV 报告",
            SuggestedFileName = suggestedName,
            FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }]
        }, cancellationToken);

    public async Task<string?> PickRecipeInputAsync(CancellationToken cancellationToken)
    {
        var paths = await interaction.PickOpenFilesAsync(
            new FilePickerOpenOptions
            {
                Title = "导入频谱遮罩配方",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("JSON 配方") { Patterns = ["*.json"] }]
            }, cancellationToken).ConfigureAwait(false);
        return paths.Count == 0 ? null : paths[0];
    }

    public Task<string?> PickRecipeOutputAsync(string suggestedName, CancellationToken cancellationToken) =>
        interaction.PickSaveFileAsync(
            new FilePickerSaveOptions
            {
                Title = "导出频谱遮罩配方",
                SuggestedFileName = suggestedName,
                FileTypeChoices = [new FilePickerFileType("JSON 配方") { Patterns = ["*.json"] }]
            }, cancellationToken);

    async Task<string?> IPeriodicNoiseFileDialog.PickRecipeInputAsync(CancellationToken cancellationToken)
    {
        var paths = await interaction.PickOpenFilesAsync(
            new FilePickerOpenOptions
            {
                Title = "导入周期陷波配方",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("JSON 配方") { Patterns = ["*.json"] }]
            }, cancellationToken).ConfigureAwait(false);
        return paths.Count == 0 ? null : paths[0];
    }

    Task<string?> IPeriodicNoiseFileDialog.PickRecipeOutputAsync(string suggestedName,
        CancellationToken cancellationToken) =>
        interaction.PickSaveFileAsync(
            new FilePickerSaveOptions
            {
                Title = "导出周期陷波配方",
                SuggestedFileName = suggestedName,
                FileTypeChoices = [new FilePickerFileType("JSON 配方") { Patterns = ["*.json"] }]
            }, cancellationToken);

    public Task<string?> PickCandidateSummaryOutputAsync(string suggestedName,
        CancellationToken cancellationToken) =>
        interaction.PickSaveFileAsync(
            new FilePickerSaveOptions
            {
                Title = "导出周期频率候选摘要",
                SuggestedFileName = suggestedName,
                FileTypeChoices = [new FilePickerFileType("JSON 摘要") { Patterns = ["*.json"] }]
            }, cancellationToken);

    public Task<string?> PickColorResultPngAsync(string suggestedName, CancellationToken cancellationToken) =>
        interaction.PickSaveFileAsync(new FilePickerSaveOptions
        {
            Title = "导出颜色实验完整尺寸 PNG", SuggestedFileName = suggestedName,
            FileTypeChoices = [new FilePickerFileType("PNG") { Patterns = ["*.png"] }]
        }, cancellationToken);

    public Task<string?> PickColorReportJsonAsync(string suggestedName, CancellationToken cancellationToken) =>
        interaction.PickSaveFileAsync(new FilePickerSaveOptions
        {
            Title = "导出颜色实验 JSON 报告", SuggestedFileName = suggestedName,
            FileTypeChoices = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }]
        }, cancellationToken);

    public Task<string?> PickColorReportCsvAsync(string suggestedName, CancellationToken cancellationToken) =>
        interaction.PickSaveFileAsync(new FilePickerSaveOptions
        {
            Title = "导出颜色实验 CSV 报告", SuggestedFileName = suggestedName,
            FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }]
        }, cancellationToken);

    public Task<string?> PickSeamResultPngAsync(string suggestedName, CancellationToken cancellationToken) =>
        interaction.PickSaveFileAsync(new FilePickerSaveOptions
        {
            Title = "导出内容感知缩放完整尺寸 PNG", SuggestedFileName = suggestedName,
            FileTypeChoices = [new FilePickerFileType("PNG") { Patterns = ["*.png"] }]
        }, cancellationToken);

    public Task<string?> PickSeamReportJsonAsync(string suggestedName, CancellationToken cancellationToken) =>
        interaction.PickSaveFileAsync(new FilePickerSaveOptions
        {
            Title = "导出内容感知缩放 JSON 报告", SuggestedFileName = suggestedName,
            FileTypeChoices = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }]
        }, cancellationToken);

    public Task<string?> PickSeamReportCsvAsync(string suggestedName, CancellationToken cancellationToken) =>
        interaction.PickSaveFileAsync(new FilePickerSaveOptions
        {
            Title = "导出内容感知缩放 CSV 步骤表", SuggestedFileName = suggestedName,
            FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }]
        }, cancellationToken);

    public Task<string?> PickPoissonResultPngAsync(string suggestedName, CancellationToken cancellationToken) =>
        interaction.PickSaveFileAsync(new FilePickerSaveOptions
        {
            Title = "导出已收敛梯度域融合 PNG", SuggestedFileName = suggestedName,
            FileTypeChoices = [new FilePickerFileType("PNG") { Patterns = ["*.png"] }]
        }, cancellationToken);

    public Task<string?> PickPoissonAlphaPngAsync(string suggestedName, CancellationToken cancellationToken) =>
        interaction.PickSaveFileAsync(new FilePickerSaveOptions
        {
            Title = "导出直接 Alpha 对照 PNG", SuggestedFileName = suggestedName,
            FileTypeChoices = [new FilePickerFileType("PNG") { Patterns = ["*.png"] }]
        }, cancellationToken);

    public Task<string?> PickPoissonReportJsonAsync(string suggestedName, CancellationToken cancellationToken) =>
        interaction.PickSaveFileAsync(new FilePickerSaveOptions
        {
            Title = "导出梯度域融合 JSON 报告", SuggestedFileName = suggestedName,
            FileTypeChoices = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }]
        }, cancellationToken);

    public Task<string?> PickPoissonReportCsvAsync(string suggestedName, CancellationToken cancellationToken) =>
        interaction.PickSaveFileAsync(new FilePickerSaveOptions
        {
            Title = "导出梯度域融合 CSV 残差表", SuggestedFileName = suggestedName,
            FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }]
        }, cancellationToken);

    public Task<string?> PickSpectralResultPngAsync(string suggestedName, CancellationToken cancellationToken) =>
        interaction.PickSaveFileAsync(new FilePickerSaveOptions { Title = "导出 Spectral Art PNG", SuggestedFileName = suggestedName, FileTypeChoices = [new FilePickerFileType("PNG") { Patterns = ["*.png"] }] }, cancellationToken);

    public async Task<string?> PickSpectralRecipeInputAsync(CancellationToken cancellationToken)
    {
        var paths = await interaction.PickOpenFilesAsync(new FilePickerOpenOptions { Title = "导入 Spectral Art 配方", AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("JSON 配方") { Patterns = ["*.json"] }] }, cancellationToken).ConfigureAwait(false);
        return paths.Count == 0 ? null : paths[0];
    }

    public Task<string?> PickSpectralRecipeOutputAsync(string suggestedName, CancellationToken cancellationToken) =>
        interaction.PickSaveFileAsync(new FilePickerSaveOptions { Title = "导出 Spectral Art 配方", SuggestedFileName = suggestedName, FileTypeChoices = [new FilePickerFileType("JSON 配方") { Patterns = ["*.json"] }] }, cancellationToken);

    public Task<string?> PickSpectralReportJsonAsync(string suggestedName, CancellationToken cancellationToken) =>
        interaction.PickSaveFileAsync(new FilePickerSaveOptions { Title = "导出 Spectral Art JSON 报告", SuggestedFileName = suggestedName, FileTypeChoices = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }] }, cancellationToken);

    public Task<string?> PickSpectralReportCsvAsync(string suggestedName, CancellationToken cancellationToken) =>
        interaction.PickSaveFileAsync(new FilePickerSaveOptions { Title = "导出 Spectral Art CSV 报告", SuggestedFileName = suggestedName, FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }] }, cancellationToken);

    public Task<bool> TrySetTextAsync(string text, CancellationToken cancellationToken) =>
        interaction.TrySetClipboardTextAsync(text, cancellationToken);
}
