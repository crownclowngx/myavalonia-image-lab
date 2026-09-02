using System.Security.Cryptography;
using ImageLabPlugin.Application.Robustness;
using ImageLabPlugin.Application.Watermarking;
using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Domain.Robustness;
using ImageLabPlugin.Domain.Watermarking;
using ImageLabPlugin.Infrastructure.Watermarking;

namespace ImageLabPlugin.Infrastructure.Robustness;

/// <summary>复用正式 Carrier 和提取用例，只补充受控实验所需的投票前/后观察，不实现第二套提取协议。</summary>
internal sealed class WatermarkDiagnosticReader(FrequencyWatermarkCarrier carrier, WatermarkFrameProtocol protocol, IExtractWatermarkUseCase extractor) : IWatermarkDiagnosticReader
{
    public WatermarkDiagnosticResult Read(PixelImage image, ControlledWatermarkBaseline baseline, ReadOnlySpan<byte> expectedPayload, string? password, CancellationToken token)
    {
        baseline.ThrowIfDisposed(); PhysicalChannelRead? headerRead = null; PhysicalChannelRead? dataRead = null; int? headerCorrections = null; int? dataCorrections = null;
        try { headerRead = carrier.ReadHeaderChannel(image, token); try { _ = protocol.DecodeHeader(headerRead.VotedBytes, out var corrected); headerCorrections = corrected; } catch (Exception e) when (e is InvalidDataException or ArgumentException or NotSupportedException) { } }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException)
        {
            return new(false, RobustnessDetectionStatus.NoSupportedWatermark, RobustnessIntegrityStatus.NotChecked, false, null, null, RobustnessFailureReason.InsufficientCarrierSlots, exception.GetType().Name);
        }
        var headerDiagnostic = ToDiagnostic(headerRead, baseline.Frame.EncodedHeader, FrequencyWatermarkCarrier.HeaderRedundancy, headerCorrections);
        try
        {
            dataRead = carrier.ReadDataChannel(image, baseline.Frame.Header, baseline.Frame.MappingKey, token);
            try
            {
                var decoded = protocol.DecodeData(baseline.Frame.Header, dataRead.VotedBytes, password);
                dataCorrections = decoded.CorrectedSymbols; decoded.Payload.Dispose(); CryptographicOperations.ZeroMemory(decoded.MappingKey);
            }
            catch (Exception exception) when (exception is InvalidDataException or ArgumentException or CryptographicException or UnauthorizedAccessException) { }
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException)
        {
            var formalWithoutData = extractor.Extract(image, password, token);
            return new(false,
                RobustnessModelMapper.ToRobustnessStatus(formalWithoutData.Status),
                RobustnessModelMapper.ToRobustnessIntegrity(formalWithoutData.Integrity),
                false, headerDiagnostic, null, RobustnessFailureReason.InsufficientCarrierSlots, exception.GetType().Name);
        }
        var dataDiagnostic = ToDiagnostic(dataRead, baseline.Frame.EncodedData, EmbeddingProfile.Resolve(baseline.Profile).DataRedundancy, dataCorrections);
        var formal = extractor.Extract(image, password, token); var matches = formal.Payload.Span.SequenceEqual(expectedPayload);
        var success = formal.Status is WatermarkDetectionStatus.RecoveredIntegrityValid or WatermarkDetectionStatus.RecoveredWithCorrections && formal.Integrity == IntegrityStatus.Valid && matches;
        return new(success,
            RobustnessModelMapper.ToRobustnessStatus(formal.Status),
            RobustnessModelMapper.ToRobustnessIntegrity(formal.Integrity),
            matches, headerDiagnostic, dataDiagnostic, success ? RobustnessFailureReason.None : Classify(formal, matches), formal.Status.ToString());
    }

    private static ChannelDiagnostic ToDiagnostic(PhysicalChannelRead read, ReadOnlySpan<byte> expected, int redundancy, int? corrections)
    {
        var ber = ChannelBerCalculator.Compare(read.PhysicalBits, read.VotedBytes, expected, redundancy);
        return new(ber.Physical, ber.Voted, corrections, read.MeanConfidence, read.P10Confidence);
    }
    private static RobustnessFailureReason Classify(ExtractionReport report, bool matches) => report.Status switch
    {
        WatermarkDetectionStatus.NoSupportedWatermark => RobustnessFailureReason.HeaderNotDetected,
        WatermarkDetectionStatus.DetectedKeyRequired => RobustnessFailureReason.KeyRequired,
        WatermarkDetectionStatus.UnsupportedVersionOrProfile => RobustnessFailureReason.UnsupportedProtocol,
        WatermarkDetectionStatus.AuthenticationFailed => RobustnessFailureReason.AuthenticationFailed,
        _ when report.Integrity == IntegrityStatus.Invalid => RobustnessFailureReason.IntegrityInvalid,
        _ when !matches && !report.Payload.IsEmpty => RobustnessFailureReason.PayloadMismatch,
        _ => RobustnessFailureReason.DataUnrecoverable
    };
}
