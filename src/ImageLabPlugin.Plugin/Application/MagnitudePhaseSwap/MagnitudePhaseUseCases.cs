using System.Diagnostics;
using System.Numerics;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.MagnitudePhaseSwap;

namespace ImageLabPlugin.Application.MagnitudePhaseSwap;

/// <summary>各解码一次 A/B，建立共同规范画布、只读频谱及共享量程源预览。</summary>
internal sealed class PrepareMagnitudePhasePairUseCase(
    IImageCodec codec, FrequencyPairCanvasProjector canvasProjector,
    MagnitudePhaseSpectrumBuilder spectrumBuilder, MagnitudePhaseSpectrumProjector spectrumProjector,
    MagnitudePhaseResourceEstimator resourceEstimator) : IPrepareMagnitudePhasePairUseCase
{
    public async Task<MagnitudePhaseSession> ExecuteAsync(PrepareMagnitudePhasePairRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PathA);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PathB);
        resourceEstimator.EnsureWithinBudget(request.CanvasSize);
        var sourceA = await codec.DecodeAsync(request.PathA, cancellationToken).ConfigureAwait(false);
        var sourceB = await codec.DecodeAsync(request.PathB, cancellationToken).ConfigureAwait(false);
        return await Task.Run(() =>
        {
            var canvasA = canvasProjector.Project(sourceA, request.CanvasSize, cancellationToken);
            var canvasB = canvasProjector.Project(sourceB, request.CanvasSize, cancellationToken);
            var spectrumA = spectrumBuilder.Build(canvasA, cancellationToken);
            var spectrumB = spectrumBuilder.Build(canvasB, cancellationToken);
            var scale = spectrumProjector.CreateSourceScale(spectrumA, spectrumB);
            return new MagnitudePhaseSession(request.PathA, request.PathB, canvasA, canvasB, spectrumA, spectrumB,
                canvasA.CreatePreview(), canvasB.CreatePreview(),
                spectrumProjector.Magnitude(spectrumA, scale, cancellationToken),
                spectrumProjector.Magnitude(spectrumB, scale, cancellationToken),
                spectrumProjector.Phase(spectrumA, cancellationToken),
                spectrumProjector.Phase(spectrumB, cancellationToken));
        }, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>按固定顺序协调混合、频谱投影、IFFT、空间投影和诊断。</summary>
/// <remarks>
/// 本类是本产品的 Application Facade，不是可插拔算法流水线。候选在所有阶段成功后才返回；调用方仍须使用
/// Session generation 原子提交。结果频谱先投影并记录能量，再由 IFFT 原地消费，峰值始终只有一个工作副本。
/// </remarks>
internal sealed class RenderMagnitudePhaseExperimentUseCase(
    SpectrumComponentMixer mixer, MagnitudePhaseSpectrumProjector spectrumProjector,
    MagnitudePhaseReconstructor reconstructor, MagnitudePhaseDisplayProjector displayProjector,
    MagnitudePhaseDiagnostics diagnostics) : IRenderMagnitudePhaseExperimentUseCase
{
    public Task<MagnitudePhaseRenderResult> ExecuteAsync(MagnitudePhaseSession session,
        MagnitudePhaseRecipe recipe, long generation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(recipe);
        session.ThrowIfDisposed();
        if (recipe.CanvasSize != session.CanvasA.Size) throw new ArgumentException("配方画布与当前 Session 不一致。", nameof(recipe));
        return Task.Run(() => Render(session, recipe, generation, cancellationToken), cancellationToken);
    }

    private MagnitudePhaseRenderResult Render(MagnitudePhaseSession session, MagnitudePhaseRecipe recipe,
        long generation, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        var mixed = mixer.Mix(session.SpectrumA, session.SpectrumB, recipe, cancellationToken);
        var sourceScale = spectrumProjector.CreateSourceScale(session.SpectrumA, session.SpectrumB);
        var scale = spectrumProjector.ExtendScale(session.SpectrumA, mixed.OwnedSpectrum, sourceScale);
        var magnitude = spectrumProjector.Magnitude(session.SpectrumA, mixed.OwnedSpectrum, scale, cancellationToken);
        var phase = spectrumProjector.Phase(session.SpectrumA, mixed.OwnedSpectrum, cancellationToken);
        var energy = CaptureEnergy(mixed.OwnedSpectrum);
        var raw = reconstructor.Reconstruct(mixed.OwnedSpectrum, recipe.CanvasSize, cancellationToken);
        var resultEnergy = CompleteEnergy(energy, raw.Values.Span);
        var projection = displayProjector.Project(raw, recipe.ProjectionKind, cancellationToken);
        var facts = diagnostics.Analyze(session.CanvasA, session.CanvasB, session.SpectrumA, session.SpectrumB,
            resultEnergy, raw, projection, mixed.Diagnostics, cancellationToken);
        return new MagnitudePhaseRenderResult(session.SessionFingerprint, recipe.Fingerprint(), generation,
            recipe, projection.Image, magnitude, phase, facts, projection.DiagnosticLabel, watch.Elapsed);
    }

    private static (double Dc, double Spectrum) CaptureEnergy(ReadOnlySpan<Complex> spectrum)
    {
        double total = 0d;
        foreach (var value in spectrum) total += value.Magnitude * value.Magnitude;
        return (spectrum[0].Magnitude, total);
    }

    private static MagnitudePhaseEnergyDiagnostics CompleteEnergy((double Dc, double Spectrum) facts,
        ReadOnlySpan<double> spatial)
    {
        double spatialEnergy = 0d;
        foreach (var value in spatial) spatialEnergy += value * value;
        var normalized = facts.Spectrum / spatial.Length;
        return new MagnitudePhaseEnergyDiagnostics(facts.Dc, facts.Spectrum, spatialEnergy,
            Math.Abs(normalized - spatialEnergy) / Math.Max(1e-30, spatialEnergy));
    }
}

/// <summary>以当前只读 A/B 频谱和已提交配方即时重建单个频点，不保存第三份完整结果频谱。</summary>
internal sealed class InspectMagnitudePhasePointUseCase(SpectrumComponentMixer mixer) : IInspectMagnitudePhasePointUseCase
{
    public MagnitudePhaseFrequencyProbe Execute(MagnitudePhaseSession session, MagnitudePhaseRecipe recipe,
        int displayX, int displayY)
    {
        session.ThrowIfDisposed();
        return mixer.Inspect(session.SpectrumA, session.SpectrumB, recipe, displayX, displayY,
            session.PhaseThresholdA, session.PhaseThresholdB);
    }
}
