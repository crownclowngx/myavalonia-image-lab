using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Application.Watermarking;
using ImageLabPlugin.Domain.Watermarking;
using MyAvaloniaManagement.PluginSdk;

namespace ImageLabPlugin.Features.WatermarkEmbed;

/// <summary>“水印写入”Document：只拥有当前作业状态，把算法执行交给 Application 用例。</summary>
/// <remarks>
/// Document 是 scoped 多实例对象。它不保存另一个 Document、Provider 或 Dock，也不在快照中保存密码和
/// 默认明文 Payload。所有长操作都与 Host ClosingToken 连接，关闭后迟到结果不会提交到当前实例。
/// </remarks>
internal sealed partial class WatermarkEmbedDocument : ObservableObject, IPersistablePluginDocument, IDisposable
{
    private const int SnapshotSchema = 1;
    private readonly IEstimateWatermarkCapacityUseCase _estimateUseCase;
    private readonly IEmbedWatermarkUseCase _embedUseCase;
    private readonly IImageLabFileDialog _fileDialog;
    private readonly IAtomicFileWriter _fileWriter;
    private readonly IDocumentLifetime _lifetime;
    private DocumentPresentationState _presentation = new("水印写入");
    private CancellationTokenSource? _operationCancellation;
    private byte[]? _outputBytes;
    private long _revision;
    private long _acceptedRevision;
    private bool _restoring;

    public WatermarkEmbedDocument(
        IEstimateWatermarkCapacityUseCase estimateUseCase,
        IEmbedWatermarkUseCase embedUseCase,
        IImageLabFileDialog fileDialog,
        IAtomicFileWriter fileWriter,
        IDocumentLifetime lifetime)
    {
        _estimateUseCase = estimateUseCase;
        _embedUseCase = embedUseCase;
        _fileDialog = fileDialog;
        _fileWriter = fileWriter;
        _lifetime = lifetime;
    }

    [ObservableProperty] private string _sourcePath = string.Empty;
    [ObservableProperty] private string _payloadText = string.Empty;
    [ObservableProperty] private string _payloadFilePath = string.Empty;
    [ObservableProperty] private bool _usePayloadFile;
    [ObservableProperty] private bool _treatTextAsJson;
    [ObservableProperty] private string _selectedProfile = "均衡";
    [ObservableProperty] private bool _useEncryption;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private bool _outputJpeg;
    [ObservableProperty] private int _jpegQuality = 95;
    [ObservableProperty] private string _capacitySummary = "请选择图片并估算容量。";
    [ObservableProperty] private string _statusMessage = "就绪";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private Bitmap? _sourcePreview;
    [ObservableProperty] private Bitmap? _outputPreview;
    [ObservableProperty] private Bitmap? _differencePreview;
    [ObservableProperty] private Bitmap? _spectrumPreview;

    public IReadOnlyList<string> ProfileOptions { get; } = ["隐蔽", "均衡", "鲁棒"];
    public DocumentPresentationState Presentation => _presentation;
    public bool IsDirty => _revision != _acceptedRevision;
    public bool HasOutput => _outputBytes is { Length: > 0 };

    public event EventHandler? PresentationChanged;
    public event EventHandler? IsDirtyChanged;

    public ValueTask InitializeAsync(DocumentActivation activation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(activation);
        cancellationToken.ThrowIfCancellationRequested();
        _restoring = true;
        try
        {
            if (activation is RestoreDocumentActivation restore)
            {
                Restore(restore.RestoredContent);
            }

            _presentation = new DocumentPresentationState(
                string.IsNullOrWhiteSpace(activation.Title) ? "水印写入" : activation.Title);
            PresentationChanged?.Invoke(this, EventArgs.Empty);
            _revision = 0;
            _acceptedRevision = 0;
        }
        finally
        {
            _restoring = false;
        }

        return ValueTask.CompletedTask;
    }

    [RelayCommand]
    private async Task SelectSourceAsync()
    {
        var path = await _fileDialog.PickImageAsync(_lifetime.ClosingToken).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(path))
        {
            SourcePath = path;
            ReplaceSourcePreview(path);
            InvalidateOutput();
        }
    }

    [RelayCommand]
    private async Task SelectPayloadFileAsync()
    {
        var path = await _fileDialog.PickPayloadAsync(_lifetime.ClosingToken).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(path))
        {
            PayloadFilePath = path;
            UsePayloadFile = true;
            InvalidateOutput();
        }
    }

    [RelayCommand]
    private async Task EstimateCapacityAsync()
    {
        await RunOperationAsync(async token =>
        {
            ValidateInputs(requirePayload: false);
            var payloadLength = await GetPayloadLengthAsync(token).ConfigureAwait(false);
            var estimate = await _estimateUseCase.ExecuteAsync(
                SourcePath,
                ResolveProfile(),
                payloadLength,
                UseEncryption,
                token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            CapacitySummary = $"载体 {estimate.CarrierSlots:N0} 槽；最大 Payload {estimate.MaximumPayloadBytes:N0} B；" +
                $"当前约 {estimate.RequiredPayloadBytes:N0} B；{(estimate.Fits ? "容量充足" : "容量不足")}";
            StatusMessage = estimate.Fits ? "容量估算完成。" : "容量不足，请减小 Payload 或更换更大图片。";
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task EmbedAsync()
    {
        await RunOperationAsync(async token =>
        {
            ValidateInputs(requirePayload: true);
            var payload = await ReadPayloadAsync(token).ConfigureAwait(false);
            try
            {
                var result = await _embedUseCase.ExecuteAsync(
                    new EmbedWatermarkRequest(
                        SourcePath,
                        payload,
                        ResolveProfile(),
                        UseEncryption ? Password : null,
                        OutputJpeg ? ImageOutputFormat.Jpeg : ImageOutputFormat.Png,
                        JpegQuality),
                    token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                _outputBytes = result.EncodedImage;
                ReplaceOutputPreview(result.EncodedImage);
                ReplaceDifferencePreview(result.DifferencePreviewPng);
                ReplaceSpectrumPreview(result.SpectrumPreviewPng);
                CapacitySummary = $"最大 {result.Capacity.MaximumPayloadBytes:N0} B；已使用约 {result.Capacity.RequiredPayloadBytes:N0} B";
                StatusMessage = $"写入和回读自检通过；PSNR {FormatMetric(result.Quality.Psnr)} dB，SSIM {result.Quality.Ssim:F4}。";
                OnPropertyChanged(nameof(HasOutput));
            }
            finally
            {
                // Document 是明文 Payload 生命周期的边界，算法返回后立即清除其私有缓冲区。
                payload.Dispose();
            }
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task SaveOutputAsync()
    {
        if (_outputBytes is not { Length: > 0 })
        {
            StatusMessage = "请先生成并通过回读自检。";
            return;
        }

        var baseName = string.IsNullOrWhiteSpace(SourcePath)
            ? "watermarked"
            : Path.GetFileNameWithoutExtension(SourcePath) + ".watermarked";
        var extension = OutputJpeg ? ".jpg" : ".png";
        var path = await _fileDialog.PickOutputImageAsync(baseName + extension, _lifetime.ClosingToken)
            .ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await RunOperationAsync(async token =>
        {
            await _fileWriter.WriteAsync(path, _outputBytes, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            StatusMessage = $"已保存：{path}";
        }).ConfigureAwait(true);
    }

    public ValueTask<DocumentSaveSnapshot> CaptureSaveSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var payload = JsonSerializer.SerializeToElement(new Snapshot(
            SourcePath,
            UsePayloadFile,
            PayloadFilePath,
            TreatTextAsJson,
            SelectedProfile,
            UseEncryption,
            OutputJpeg,
            JpegQuality));
        return ValueTask.FromResult(new DocumentSaveSnapshot(
            new DocumentRevision(_revision),
            new DocumentContent(SnapshotSchema, payload)));
    }

    public void AcceptChanges(DocumentRevision savedRevision)
    {
        var wasDirty = IsDirty;
        if (savedRevision.Value == _revision)
        {
            _acceptedRevision = _revision;
        }

        if (wasDirty != IsDirty)
        {
            IsDirtyChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        SourcePreview?.Dispose();
        OutputPreview?.Dispose();
        DifferencePreview?.Dispose();
        SpectrumPreview?.Dispose();
        Password = string.Empty;
        if (_outputBytes is not null)
        {
            CryptographicOperations.ZeroMemory(_outputBytes);
            _outputBytes = null;
        }
    }

    partial void OnSourcePathChanged(string value) => MarkChangedAndInvalidate();
    partial void OnPayloadTextChanged(string value) => MarkChangedAndInvalidate();
    partial void OnPayloadFilePathChanged(string value) => MarkChangedAndInvalidate();
    partial void OnUsePayloadFileChanged(bool value) => MarkChangedAndInvalidate();
    partial void OnTreatTextAsJsonChanged(bool value) => MarkChangedAndInvalidate();
    partial void OnSelectedProfileChanged(string value) => MarkChangedAndInvalidate();
    partial void OnUseEncryptionChanged(bool value) => MarkChangedAndInvalidate();
    partial void OnPasswordChanged(string value) => InvalidateOutput();
    partial void OnOutputJpegChanged(bool value) => MarkChangedAndInvalidate();
    partial void OnJpegQualityChanged(int value) => MarkChangedAndInvalidate();

    [RelayCommand]
    private void Cancel() => _operationCancellation?.Cancel();

    private async Task RunOperationAsync(Func<CancellationToken, Task> operation)
    {
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ClosingToken);
        var current = _operationCancellation;
        IsBusy = true;
        try
        {
            await operation(current.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
            if (!_lifetime.IsClosing)
            {
                StatusMessage = "操作已取消。";
            }
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
        finally
        {
            if (ReferenceEquals(_operationCancellation, current))
            {
                IsBusy = false;
            }
        }
    }

    private void ValidateInputs(bool requirePayload)
    {
        if (string.IsNullOrWhiteSpace(SourcePath) || !File.Exists(SourcePath))
        {
            throw new InvalidOperationException("请选择存在的 PNG 或 JPEG 源图片。");
        }

        if (UseEncryption && string.IsNullOrEmpty(Password))
        {
            throw new InvalidOperationException("启用密码保护时必须输入密码。");
        }

        if (requirePayload && UsePayloadFile && !File.Exists(PayloadFilePath))
        {
            throw new InvalidOperationException("请选择存在的 Payload 文件。");
        }

        if (requirePayload && !UsePayloadFile && string.IsNullOrEmpty(PayloadText))
        {
            throw new InvalidOperationException("请输入文本/JSON Payload，或选择一个 Payload 文件。");
        }
    }

    private async Task<int> GetPayloadLengthAsync(CancellationToken cancellationToken)
    {
        if (UsePayloadFile)
        {
            if (!File.Exists(PayloadFilePath))
            {
                throw new InvalidOperationException("请选择存在的 Payload 文件。");
            }

            var length = new FileInfo(PayloadFilePath).Length;
            if (length > WatermarkPayload.MaximumPayloadBytes)
            {
                throw new InvalidOperationException("Payload 文件超过 V1 的 16 MiB 绝对上限。");
            }

            return checked((int)length);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Encoding.UTF8.GetByteCount(PayloadText);
    }

    private async Task<WatermarkPayload> ReadPayloadAsync(CancellationToken cancellationToken)
    {
        if (UsePayloadFile)
        {
            var bytes = await File.ReadAllBytesAsync(PayloadFilePath, cancellationToken).ConfigureAwait(false);
            return new WatermarkPayload(bytes, PayloadContentType.Binary);
        }

        if (TreatTextAsJson)
        {
            using var _ = JsonDocument.Parse(PayloadText);
        }

        return new WatermarkPayload(
            Encoding.UTF8.GetBytes(PayloadText),
            TreatTextAsJson ? PayloadContentType.Json : PayloadContentType.Text);
    }

    private EmbeddingProfileId ResolveProfile() => SelectedProfile switch
    {
        "隐蔽" => EmbeddingProfileId.Stealth,
        "鲁棒" => EmbeddingProfileId.Robust,
        _ => EmbeddingProfileId.Balanced
    };

    private void Restore(DocumentContent content)
    {
        if (content.SchemaVersion != SnapshotSchema)
        {
            throw new NotSupportedException($"不支持水印写入 Document schema {content.SchemaVersion}。");
        }

        var snapshot = content.Payload.Deserialize<Snapshot>()
            ?? throw new InvalidDataException("水印写入 Document 快照为空。");
        SourcePath = snapshot.SourcePath ?? string.Empty;
        UsePayloadFile = snapshot.UsePayloadFile;
        PayloadFilePath = snapshot.PayloadFilePath ?? string.Empty;
        TreatTextAsJson = snapshot.TreatTextAsJson;
        SelectedProfile = ProfileOptions.Contains(snapshot.SelectedProfile) ? snapshot.SelectedProfile! : "均衡";
        UseEncryption = snapshot.UseEncryption;
        OutputJpeg = snapshot.OutputJpeg;
        JpegQuality = Math.Clamp(snapshot.JpegQuality, 1, 100);
        PayloadText = string.Empty;
        Password = string.Empty;
        StatusMessage = File.Exists(SourcePath) ? "已恢复作业配方，请重新输入未保存的 Payload/密码。" : "源图片不存在，请重新选择。";
        if (File.Exists(SourcePath))
        {
            ReplaceSourcePreview(SourcePath);
        }
    }

    private void ReplaceSourcePreview(string path)
    {
        var replacement = new Bitmap(path);
        var previous = SourcePreview;
        SourcePreview = replacement;
        previous?.Dispose();
    }

    private void ReplaceOutputPreview(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var replacement = new Bitmap(stream);
        var previous = OutputPreview;
        OutputPreview = replacement;
        previous?.Dispose();
    }

    private void ReplaceDifferencePreview(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var replacement = new Bitmap(stream);
        var previous = DifferencePreview;
        DifferencePreview = replacement;
        previous?.Dispose();
    }

    private void ReplaceSpectrumPreview(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var replacement = new Bitmap(stream);
        var previous = SpectrumPreview;
        SpectrumPreview = replacement;
        previous?.Dispose();
    }

    private void MarkChangedAndInvalidate()
    {
        if (_restoring)
        {
            return;
        }

        var wasDirty = IsDirty;
        _revision++;
        if (!wasDirty)
        {
            IsDirtyChanged?.Invoke(this, EventArgs.Empty);
        }

        InvalidateOutput();
    }

    private void InvalidateOutput()
    {
        if (_outputBytes is not null)
        {
            CryptographicOperations.ZeroMemory(_outputBytes);
            _outputBytes = null;
            OnPropertyChanged(nameof(HasOutput));
        }

        var previous = OutputPreview;
        OutputPreview = null;
        previous?.Dispose();
        previous = DifferencePreview;
        DifferencePreview = null;
        previous?.Dispose();
        previous = SpectrumPreview;
        SpectrumPreview = null;
        previous?.Dispose();
    }

    private static string FormatMetric(double value) => double.IsPositiveInfinity(value) ? "∞" : value.ToString("F2");

    private sealed record Snapshot(
        string? SourcePath,
        bool UsePayloadFile,
        string? PayloadFilePath,
        bool TreatTextAsJson,
        string? SelectedProfile,
        bool UseEncryption,
        bool OutputJpeg,
        int JpegQuality);
}
