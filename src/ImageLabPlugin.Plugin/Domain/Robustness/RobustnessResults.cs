using ImageLabPlugin.Domain.Comparison;
using ImageLabPlugin.Domain.Imaging;
using ImageLabPlugin.Domain.Watermarking;
using System.Numerics;
using ImageLabPlugin.Domain.Fingerprinting;

namespace ImageLabPlugin.Domain.Robustness;

internal enum RobustnessFailureReason
{
    None,
    HeaderNotDetected,
    KeyRequired,
    UnsupportedProtocol,
    InsufficientCarrierSlots,
    DataUnrecoverable,
    IntegrityInvalid,
    PayloadMismatch,
    AuthenticationFailed,
    OperatorFailed
}

internal readonly record struct BerMeasurement(long ErrorBits, long ComparedBits)
{
    public double? Ratio => ComparedBits == 0 ? null : ErrorBits / (double)ComparedBits;
}

/// <summary>BER 的纯计算核心，供真实 Carrier 诊断与人工 bit Golden Vector 共用。</summary>
internal static class ChannelBerCalculator
{
    public static (BerMeasurement Physical, BerMeasurement Voted) Compare(ReadOnlySpan<bool> physicalBits, ReadOnlySpan<byte> votedBytes, ReadOnlySpan<byte> expectedBytes, int redundancy)
    {
        if (redundancy <= 0) throw new ArgumentOutOfRangeException(nameof(redundancy));
        var comparablePhysical = Math.Min(physicalBits.Length, checked(expectedBytes.Length * 8 * redundancy)); long physicalErrors = 0;
        for (var index = 0; index < comparablePhysical; index++) if (physicalBits[index] != ReadBit(expectedBytes, index / redundancy)) physicalErrors++;
        var comparableBytes = Math.Min(expectedBytes.Length, votedBytes.Length); long votedErrors = 0;
        for (var i = 0; i < comparableBytes; i++) votedErrors += BitOperations.PopCount((uint)(expectedBytes[i] ^ votedBytes[i]));
        return (new(physicalErrors, comparablePhysical), new(votedErrors, comparableBytes * 8L));
    }
    private static bool ReadBit(ReadOnlySpan<byte> bytes, int bit) => (bytes[bit / 8] & (1 << (7 - bit % 8))) != 0;
}

internal sealed record ChannelDiagnostic(
    BerMeasurement PhysicalRawBer,
    BerMeasurement VotedPreEccBer,
    int? CorrectedSymbols,
    double MeanConfidence,
    double P10Confidence);

internal sealed record WatermarkDiagnosticResult(
    bool Success,
    WatermarkDetectionStatus DetectionStatus,
    IntegrityStatus Integrity,
    bool PayloadMatches,
    ChannelDiagnostic? Header,
    ChannelDiagnostic? Data,
    RobustnessFailureReason FailureReason,
    string TechnicalReason);

internal enum QualityUnavailableReason { SizeMismatch, OperatorFailed }
internal sealed record QualityMeasurement(FullReferenceQualityMetrics? Metrics, QualityUnavailableReason? UnavailableReason)
{
    public bool IsAvailable => Metrics is not null;
}

internal sealed record LocalQualityCell(int Column, int Row, double MeanAbsoluteErrorRgb, double MeanAbsoluteErrorLuma, int MaximumAbsoluteErrorRgb, double ChangedPixelRatio);

internal sealed record RobustnessObservation(string StepId, int StepIndex, WatermarkDiagnosticResult Diagnostic);

internal sealed record RobustnessCaseResult(
    RobustnessCaseKey Key,
    bool Completed,
    WatermarkDiagnosticResult? FinalDiagnostic,
    IReadOnlyList<RobustnessObservation> Observations,
    string? FirstObservedUnrecoverableStep,
    bool RecoveredAfterFailure,
    QualityMeasurement AttackOnlyQuality,
    QualityMeasurement EndToEndQuality,
    IReadOnlyList<LocalQualityCell> LocalQuality,
    long? JpegEncodedBytes = null,
    string? OperatorError = null,
    IReadOnlyList<FingerprintObservation>? FingerprintObservations = null);

internal sealed record RobustnessCurvePoint(
    EmbeddingProfileId Profile,
    decimal ScanValue,
    int CompletedTrials,
    int Successes,
    double? SuccessRate,
    double? MedianVotedBer,
    double? MedianConfidence,
    IReadOnlyDictionary<RobustnessFailureReason, int> FailureCounts);

internal sealed record RobustnessStepFact(string StepId, string KindId, bool Enabled, string Parameters);
internal sealed record RobustnessRecipeFacts(
    int SchemaVersion,
    IReadOnlyList<RobustnessStepFact> OrderedSteps,
    string ScanStepId,
    string ScanParameterId,
    IReadOnlyList<decimal> ScanPoints,
    int TrialCount,
    IReadOnlyList<EmbeddingProfileId> Profiles,
    bool ProbeEachStep);

internal sealed record RobustnessExperimentReport(
    int SchemaVersion,
    string RecipeHash,
    DateTimeOffset CompletedAtUtc,
    bool IsComplete,
    ulong ExperimentSeed,
    string RandomAlgorithm,
    string SourceName,
    int PayloadLength,
    string PayloadDigestId,
    RobustnessRecipeFacts Recipe,
    IReadOnlyList<RobustnessCaseResult> Cases,
    IReadOnlyList<RobustnessCurvePoint> Curves);

internal static class RobustnessResultAggregator
{
    public static IReadOnlyList<RobustnessCurvePoint> Aggregate(IEnumerable<RobustnessCaseResult> cases) => cases
        .Where(item => item.Completed)
        .GroupBy(item => (item.Key.Profile, item.Key.CanonicalValue))
        .OrderBy(group => group.Key.Profile).ThenBy(group => group.Key.CanonicalValue)
        .Select(group =>
        {
            var values = group.ToArray(); var successes = values.Count(item => item.FinalDiagnostic?.Success == true);
            var ber = values.Select(item => item.FinalDiagnostic?.Data?.VotedPreEccBer.Ratio).Where(value => value.HasValue).Select(value => value!.Value).Order().ToArray();
            var confidence = values.Select(item => item.FinalDiagnostic?.Data?.MeanConfidence ?? item.FinalDiagnostic?.Header?.MeanConfidence).Where(value => value.HasValue).Select(value => value!.Value).Order().ToArray();
            return new RobustnessCurvePoint(group.Key.Profile, group.Key.CanonicalValue, values.Length, successes, values.Length == 0 ? null : successes / (double)values.Length,
                Median(ber), Median(confidence), values.Where(item => item.FinalDiagnostic is not null && !item.FinalDiagnostic.Success).GroupBy(item => item.FinalDiagnostic!.FailureReason).ToDictionary(item => item.Key, item => item.Count()));
        }).ToArray();

    private static double? Median(double[] values) => values.Length == 0 ? null : values.Length % 2 == 1 ? values[values.Length / 2] : (values[(values.Length / 2) - 1] + values[values.Length / 2]) / 2d;
}

/// <summary>固定 16×16 归一化网格的局部误差，不宣称是滑窗 SSIM Map。</summary>
internal static class LocalQualityGridAnalyzer
{
    public static IReadOnlyList<LocalQualityCell> Analyze(PixelImage reference, PixelImage candidate, CancellationToken token)
    {
        if (reference.Size != candidate.Size) return [];
        const int grid = 16; var columns = Math.Min(grid, reference.Size.Width); var rows = Math.Min(grid, reference.Size.Height); var result = new List<LocalQualityCell>(columns * rows);
        var a = reference.Rgba.Span; var b = candidate.Rgba.Span;
        for (var row = 0; row < rows; row++)
        {
            token.ThrowIfCancellationRequested(); var y0 = row * reference.Size.Height / rows; var y1 = (row + 1) * reference.Size.Height / rows;
            for (var column = 0; column < columns; column++)
            {
                var x0 = column * reference.Size.Width / columns; var x1 = (column + 1) * reference.Size.Width / columns;
                double absRgb = 0d, absY = 0d; long pixels = 0, changed = 0; var maximum = 0;
                for (var y = y0; y < y1; y++) for (var x = x0; x < x1; x++)
                {
                    var o = ((y * reference.Size.Width) + x) * 4; var dr = Math.Abs(b[o] - a[o]); var dg = Math.Abs(b[o + 1] - a[o + 1]); var db = Math.Abs(b[o + 2] - a[o + 2]);
                    absRgb += dr + dg + db; maximum = Math.Max(maximum, Math.Max(dr, Math.Max(dg, db))); if ((dr | dg | db) != 0) changed++;
                    absY += Math.Abs(ColorSpaceConverter.ToLuma(a[o], a[o + 1], a[o + 2]) - ColorSpaceConverter.ToLuma(b[o], b[o + 1], b[o + 2])); pixels++;
                }
                result.Add(new(column, row, absRgb / (pixels * 3d), absY / pixels, maximum, changed / (double)pixels));
            }
        }
        return result;
    }
}
