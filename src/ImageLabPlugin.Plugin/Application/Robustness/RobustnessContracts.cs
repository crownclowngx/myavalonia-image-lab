using System.Security.Cryptography;
using ImageLabPlugin.Domain.Imaging;
using ImageLabPlugin.Domain.Robustness;
using ImageLabPlugin.Domain.Watermarking;
using ImageLabPlugin.Infrastructure.Watermarking;
using ImageLabPlugin.Domain.Fingerprinting;

namespace ImageLabPlugin.Application.Robustness;

internal sealed record PrepareRobustnessBaselineRequest(string SourcePath, ReadOnlyMemory<byte> Payload, PayloadContentType ContentType, IReadOnlyList<EmbeddingProfileId> Profiles, string? Password);

/// <summary>一个 Profile 的受控事实所有者；释放时清零预期信道字节和 Mapping Key。</summary>
internal sealed class ControlledWatermarkBaseline : IDisposable
{
    private bool _disposed;
    public ControlledWatermarkBaseline(EmbeddingProfileId profile, PixelImage image, EncodedWatermarkFrame frame, ExtractionReport selfCheck)
    { Profile = profile; Image = image; Frame = frame; SelfCheck = selfCheck; }
    public EmbeddingProfileId Profile { get; }
    public PixelImage Image { get; private set; }
    public EncodedWatermarkFrame Frame { get; private set; }
    public ExtractionReport SelfCheck { get; }
    public void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(ControlledWatermarkBaseline)); }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        CryptographicOperations.ZeroMemory(Frame.EncodedHeader); CryptographicOperations.ZeroMemory(Frame.EncodedData); CryptographicOperations.ZeroMemory(Frame.MappingKey);
        Frame = new(Frame.Header, [], [], []); Image = new PixelImage(new ImageSize(1, 1), [0, 0, 0, 0]);
    }
}

internal sealed class RobustnessBaselineSession : IDisposable
{
    private bool _disposed; private byte[] _payload; private byte[] _passwordUtf8;
    public RobustnessBaselineSession(string sourceName, PixelImage original, byte[] payload, byte[] passwordUtf8, IReadOnlyDictionary<EmbeddingProfileId, ControlledWatermarkBaseline> profiles, string payloadDigestId)
    { SourceName = sourceName; Original = original; _payload = payload; _passwordUtf8 = passwordUtf8; Profiles = profiles; PayloadDigestId = payloadDigestId; }
    public string SourceName { get; }
    public PixelImage Original { get; private set; }
    public IReadOnlyDictionary<EmbeddingProfileId, ControlledWatermarkBaseline> Profiles { get; private set; }
    public string PayloadDigestId { get; }
    public int PayloadLength => _payload.Length;
    internal ReadOnlySpan<byte> Payload => _payload;
    internal string? GetPassword() => _passwordUtf8.Length == 0 ? null : System.Text.Encoding.UTF8.GetString(_passwordUtf8);
    public void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(RobustnessBaselineSession)); }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; foreach (var value in Profiles.Values) value.Dispose();
        CryptographicOperations.ZeroMemory(_payload); CryptographicOperations.ZeroMemory(_passwordUtf8); _payload = []; _passwordUtf8 = [];
        Profiles = new Dictionary<EmbeddingProfileId, ControlledWatermarkBaseline>(); Original = new PixelImage(new ImageSize(1, 1), [0, 0, 0, 0]);
    }
}

internal sealed class RobustnessExperimentSession : IDisposable
{
    private bool _disposed;
    public RobustnessExperimentSession(RobustnessExperimentReport report) => Report = report;
    public RobustnessExperimentReport Report { get; private set; }
    public bool IsDisposed => _disposed;
    public void Dispose() { if (_disposed) return; _disposed = true; Report = Report with { Cases = [], Curves = [] }; }
}

internal sealed record RobustnessProgress(int CompletedCases, int TotalCases, RobustnessCaseKey? CurrentCase);

internal interface IPrepareRobustnessBaselineUseCase
{
    Task<RobustnessBaselineSession> ExecuteAsync(PrepareRobustnessBaselineRequest request, CancellationToken cancellationToken);
}
internal interface IPlanRobustnessExperimentUseCase
{
    RobustnessExecutionPlan Execute(RobustnessRecipe recipe, IReadOnlyList<EmbeddingProfileId> profiles);
}
internal interface IRunRobustnessExperimentUseCase
{
    Task<RobustnessExperimentSession> ExecuteAsync(RobustnessBaselineSession baseline, RobustnessExecutionPlan plan, IProgress<RobustnessProgress>? progress, CancellationToken cancellationToken);

    Task<RobustnessExperimentSession> ExecuteAsync(
        RobustnessBaselineSession baseline,
        RobustnessExecutionPlan plan,
        IReadOnlyList<FingerprintAlgorithmId> fingerprintAlgorithms,
        IProgress<RobustnessProgress>? progress,
        CancellationToken cancellationToken) => ExecuteAsync(baseline, plan, progress, cancellationToken);
}

/// <summary>鲁棒性应用层的窄观测入口；只返回指纹事实，不修改实验结论。</summary>
internal interface IFingerprintObservationProbe
{
    IReadOnlyList<FingerprintObservation> Observe(PixelImage reference, PixelImage candidate, IReadOnlyList<FingerprintAlgorithmId> algorithms, CancellationToken cancellationToken);
}
internal interface IWatermarkDiagnosticReader
{
    WatermarkDiagnosticResult Read(PixelImage image, ControlledWatermarkBaseline baseline, ReadOnlySpan<byte> expectedPayload, string? password, CancellationToken cancellationToken);
}
internal interface IExportRobustnessReportUseCase
{
    Task ExportJsonAsync(RobustnessExperimentReport report, string path, CancellationToken cancellationToken);
    Task ExportCsvAsync(RobustnessExperimentReport report, string path, CancellationToken cancellationToken);
    string CreateJson(RobustnessExperimentReport report);
    string CreateCsv(RobustnessExperimentReport report);
}
