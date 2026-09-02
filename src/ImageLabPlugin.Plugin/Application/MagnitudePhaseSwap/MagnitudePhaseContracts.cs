using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Domain.MagnitudePhaseSwap;

namespace ImageLabPlugin.Application.MagnitudePhaseSwap;

/// <summary>冻结幅相交换独立协议、schema 与有界文件大小。</summary>
internal static class MagnitudePhaseProtocol
{
    public const string Recipe = "magnitude-phase-swap-v1";
    public const string Report = "magnitude-phase-swap-report-v1";
    public const int Schema = 1;
    public const int SnapshotSchema = 1;
    public const int MaximumJsonBytes = 256 * 1024;
}

internal sealed record PrepareMagnitudePhasePairRequest(string PathA, string PathB, int CanvasSize);

/// <summary>应用层候选结果；全部图片和指标同时完成后才能交给 Session 原子提交。</summary>
internal sealed record MagnitudePhaseRenderResult(
    string SessionFingerprint, string RecipeFingerprint, long Generation, MagnitudePhaseRecipe Recipe,
    PixelImage ResultImage, PixelImage ResultMagnitude, PixelImage ResultPhase,
    MagnitudePhaseDiagnosticsResult Diagnostics, string? DiagnosticLabel, TimeSpan Elapsed);

internal sealed record MagnitudePhaseSnapshotState(string? DisplayNameA, string? DisplayNameB,
    int CanvasSize, string Preset, double Amount, string SelectedPage, bool SynchronizedZoom,
    bool MetricsVisible, int Schema);

internal sealed record MagnitudePhaseReport(string Protocol, int Schema, string FingerprintA,
    string FingerprintB, string RecipeFingerprint, MagnitudePhaseRecipe Recipe,
    MagnitudePhaseDiagnosticsResult Diagnostics, long ElapsedMilliseconds,
    string ImplementationVersion, string Limitation);

internal interface IPrepareMagnitudePhasePairUseCase
{
    Task<MagnitudePhaseSession> ExecuteAsync(PrepareMagnitudePhasePairRequest request,
        CancellationToken cancellationToken);
}

internal interface IRenderMagnitudePhaseExperimentUseCase
{
    Task<MagnitudePhaseRenderResult> ExecuteAsync(MagnitudePhaseSession session,
        MagnitudePhaseRecipe recipe, long generation, CancellationToken cancellationToken);
}

internal interface IInspectMagnitudePhasePointUseCase
{
    MagnitudePhaseFrequencyProbe Execute(MagnitudePhaseSession session, MagnitudePhaseRecipe recipe,
        int displayX, int displayY);
}

internal interface IMagnitudePhaseRecipeSerializer
{
    byte[] Serialize(MagnitudePhaseRecipe recipe, string fingerprintA, string fingerprintB);
    MagnitudePhaseRecipe Deserialize(ReadOnlySpan<byte> json, out string fingerprintA, out string fingerprintB);
}

internal interface IMagnitudePhaseReportSerializer
{
    byte[] SerializeJson(MagnitudePhaseReport report);
    byte[] SerializeCsv(MagnitudePhaseReport report);
}

internal interface IMagnitudePhaseSnapshotSerializer
{
    System.Text.Json.JsonElement Serialize(MagnitudePhaseSnapshotState state);
    MagnitudePhaseSnapshotState? Deserialize(System.Text.Json.JsonElement payload);
}

internal interface IExportMagnitudePhaseImageUseCase
{
    Task ExecuteAsync(MagnitudePhaseSession session, MagnitudePhaseRenderResult result,
        string path, CancellationToken cancellationToken);
}

internal interface IImportMagnitudePhaseRecipeUseCase
{
    Task<(MagnitudePhaseRecipe Recipe, string FingerprintA, string FingerprintB)> ExecuteAsync(
        string path, CancellationToken cancellationToken);
}

internal interface IExportMagnitudePhaseRecipeUseCase
{
    Task ExecuteAsync(MagnitudePhaseRecipe recipe, MagnitudePhaseSession session,
        string path, CancellationToken cancellationToken);
}

internal interface IExportMagnitudePhaseReportUseCase
{
    Task ExecuteAsync(MagnitudePhaseReport report, MagnitudePhaseSession session, string path, bool csv,
        CancellationToken cancellationToken);
}
