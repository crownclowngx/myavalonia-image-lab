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

namespace ImageLabPlugin.Features.WatermarkInspect;

/// <summary>“提取与验证”Document：拥有当前图片的检测现场和短生命周期恢复结果。</summary>
internal sealed partial class WatermarkInspectDocument : ObservableObject, IPersistablePluginDocument, IDisposable
{
    private const int SnapshotSchema = 1;
    private const int PreviewCharacterLimit = 4096;
    private readonly IInspectWatermarkUseCase _inspectUseCase;
    private readonly IExtractWatermarkUseCase _extractUseCase;
    private readonly IImageFileDialog _imageFileDialog;
    private readonly IPayloadFileDialog _payloadFileDialog;
    private readonly IAtomicFileWriter _fileWriter;
    private readonly IDocumentLifetime _lifetime;
    private DocumentPresentationState _presentation = new("提取与验证");
    private CancellationTokenSource? _operationCancellation;
    private byte[]? _recoveredPayload;
    private PayloadContentType _recoveredContentType;
    private long _revision;
    private long _acceptedRevision;
    private bool _restoring;

    public WatermarkInspectDocument(
        IInspectWatermarkUseCase inspectUseCase,
        IExtractWatermarkUseCase extractUseCase,
        IImageFileDialog imageFileDialog,
        IPayloadFileDialog payloadFileDialog,
        IAtomicFileWriter fileWriter,
        IDocumentLifetime lifetime)
    {
        _inspectUseCase = inspectUseCase;
        _extractUseCase = extractUseCase;
        _imageFileDialog = imageFileDialog;
        _payloadFileDialog = payloadFileDialog;
        _fileWriter = fileWriter;
        _lifetime = lifetime;
    }

    [ObservableProperty] private string _sourcePath = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _detectionSummary = "请选择需要检查的图片。";
    [ObservableProperty] private string _payloadPreview = string.Empty;
    [ObservableProperty] private string _verificationReport = "尚未检测。";
    [ObservableProperty] private bool _needsPassword;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private Bitmap? _sourcePreview;

    public DocumentPresentationState Presentation => _presentation;
    public bool IsDirty => _revision != _acceptedRevision;
    public bool HasRecoveredPayload => _recoveredPayload is { Length: > 0 };

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
                string.IsNullOrWhiteSpace(activation.Title) ? "提取与验证" : activation.Title);
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
        var path = await _imageFileDialog.PickImageAsync(_lifetime.ClosingToken).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(path))
        {
            SourcePath = path;
            ReplaceSourcePreview(path);
            await InspectAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task InspectAsync()
    {
        await RunOperationAsync(async token =>
        {
            ValidateSource();
            var (_, header, report) = await _inspectUseCase.ExecuteAsync(SourcePath, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            NeedsPassword = report.Status == WatermarkDetectionStatus.DetectedKeyRequired;
            DetectionSummary = report.Summary;
            VerificationReport = header is null
                ? report.Summary
                : $"协议：V1；配置：{header.Header.Profile}；Header 修复：{header.CorrectedSymbols}；置信度：{header.Confidence:P1}";
            ClearRecoveredPayload();
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ExtractAsync()
    {
        await RunOperationAsync(async token =>
        {
            ValidateSource();
            var (_, report) = await _extractUseCase.ExecuteAsync(
                SourcePath,
                string.IsNullOrEmpty(Password) ? null : Password,
                token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            DetectionSummary = report.Summary;
            NeedsPassword = report.Status == WatermarkDetectionStatus.DetectedKeyRequired;
            VerificationReport = BuildVerificationReport(report);
            ClearRecoveredPayload();
            if (report.Status is WatermarkDetectionStatus.RecoveredIntegrityValid or WatermarkDetectionStatus.RecoveredWithCorrections)
            {
                _recoveredPayload = report.Payload.ToArray();
                _recoveredContentType = report.ContentType ?? PayloadContentType.Binary;
                PayloadPreview = FormatPayloadPreview(_recoveredPayload, _recoveredContentType);
                OnPropertyChanged(nameof(HasRecoveredPayload));
            }
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ExportPayloadAsync()
    {
        if (_recoveredPayload is not { Length: > 0 })
        {
            DetectionSummary = "没有可导出的已验证 Payload。";
            return;
        }

        var suggestedName = _recoveredContentType switch
        {
            PayloadContentType.Text => "recovered.txt",
            PayloadContentType.Json => "recovered.json",
            _ => "recovered.bin"
        };
        var path = await _payloadFileDialog.PickPayloadExportAsync(suggestedName, _lifetime.ClosingToken)
            .ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await RunOperationAsync(async token =>
        {
            await _fileWriter.WriteAsync(path, _recoveredPayload, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            DetectionSummary = $"Payload 已导出：{path}";
        }).ConfigureAwait(true);
    }

    public ValueTask<DocumentSaveSnapshot> CaptureSaveSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var payload = JsonSerializer.SerializeToElement(new Snapshot(SourcePath));
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
        Password = string.Empty;
        ClearRecoveredPayload();
    }

    partial void OnSourcePathChanged(string value)
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

        DetectionSummary = "图片已改变，请重新检测。";
        VerificationReport = "尚未检测。";
        NeedsPassword = false;
        ClearRecoveredPayload();
    }

    partial void OnPasswordChanged(string value)
    {
        // 新密码尚未验证，旧恢复结果不能继续导出或被误认为属于当前凭据。
        ClearRecoveredPayload();
    }

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
                DetectionSummary = "操作已取消。";
            }
        }
        catch (Exception exception)
        {
            DetectionSummary = exception.Message;
        }
        finally
        {
            if (ReferenceEquals(_operationCancellation, current))
            {
                IsBusy = false;
            }
        }
    }

    private void ValidateSource()
    {
        if (string.IsNullOrWhiteSpace(SourcePath) || !File.Exists(SourcePath))
        {
            throw new InvalidOperationException("请选择存在的 PNG 或 JPEG 图片。");
        }
    }

    private void Restore(DocumentContent content)
    {
        if (content.SchemaVersion != SnapshotSchema)
        {
            throw new NotSupportedException($"不支持提取与验证 Document schema {content.SchemaVersion}。");
        }

        var snapshot = content.Payload.Deserialize<Snapshot>()
            ?? throw new InvalidDataException("提取与验证 Document 快照为空。");
        SourcePath = snapshot.SourcePath ?? string.Empty;
        Password = string.Empty;
        ClearRecoveredPayload();
        if (File.Exists(SourcePath))
        {
            ReplaceSourcePreview(SourcePath);
            DetectionSummary = "已恢复图片引用，请重新检测；密码和恢复内容未保存。";
        }
        else
        {
            DetectionSummary = "原图片不存在，请重新选择。";
        }
    }

    private void ReplaceSourcePreview(string path)
    {
        var replacement = new Bitmap(path);
        var previous = SourcePreview;
        SourcePreview = replacement;
        previous?.Dispose();
    }

    private void ClearRecoveredPayload()
    {
        if (_recoveredPayload is not null)
        {
            CryptographicOperations.ZeroMemory(_recoveredPayload);
            _recoveredPayload = null;
        }

        PayloadPreview = string.Empty;
        OnPropertyChanged(nameof(HasRecoveredPayload));
    }

    private static string FormatPayloadPreview(byte[] payload, PayloadContentType contentType)
    {
        if (contentType == PayloadContentType.Binary)
        {
            var count = Math.Min(payload.Length, 256);
            var hex = Convert.ToHexString(payload.AsSpan(0, count));
            return payload.Length > count ? hex + $"\n… 共 {payload.Length:N0} 字节，请导出查看完整内容。" : hex;
        }

        var text = Encoding.UTF8.GetString(payload);
        if (contentType == PayloadContentType.Json)
        {
            using var json = JsonDocument.Parse(text);
            text = JsonSerializer.Serialize(json.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }

        return text.Length > PreviewCharacterLimit
            ? text[..PreviewCharacterLimit] + $"\n… 共 {text.Length:N0} 字符，请导出查看完整内容。"
            : text;
    }

    private static string BuildVerificationReport(ExtractionReport report) =>
        $"状态：{report.Status}\n" +
        $"配置：{report.Profile?.ToString() ?? "未知"}\n" +
        $"完整性：{report.Integrity}\n" +
        $"来源真实性：{report.Authenticity}（V1 未签名）\n" +
        $"ECC 修复符号：{report.CorrectedSymbols}\n" +
        $"通道置信度：{report.Confidence:P1}";

    private sealed record Snapshot(string? SourcePath);
}
