using ImageLabPlugin.Application.Wavelets;
using ImageLabPlugin.Domain.Imaging;
using ImageLabPlugin.Domain.Watermarking;
using ImageLabPlugin.Domain.Wavelets;
using ImageLabPlugin.Domain.Robustness;
using ImageLabPlugin.Infrastructure.Watermarking;

namespace ImageLabPlugin.Infrastructure.Wavelets;

/// <summary>把既有 DCT Frame/Carrier 适配到公平比较窄端口，不改变任何 DCT Golden 或槽位协议。</summary>
internal sealed class DctWatermarkBenchmarkAdapter(
    FrequencyWatermarkCarrier carrier,
    WatermarkFrameProtocol frameProtocol) : IWatermarkBenchmarkCarrier
{
    public string CarrierId => "dct-frequency-qim-v1";

    public WatermarkBenchmarkCapacity Estimate(PixelImage source, int payloadLength)
    {
        var estimate = carrier.Estimate(source, EmbeddingProfileId.Balanced, payloadLength, encrypted: false);
        return new(CarrierId, estimate.MaximumPayloadBytes);
    }

    public Task<WatermarkBenchmarkEmbedding> EmbedAndReadAsync(PixelImage source, ReadOnlyMemory<byte> payloadBytes,
        CancellationToken cancellationToken) => Task.Run(() =>
    {
        using var payload = new WatermarkPayload(payloadBytes, PayloadContentType.Binary);
        var frame = frameProtocol.Encode(payload, EmbeddingProfileId.Balanced, password: null);
        var marked = carrier.Embed(source, frame, cancellationToken);
        var read = ReadCore(marked, cancellationToken);
        var rawBer = MeasureRawBer(marked, frame, cancellationToken);
        return new WatermarkBenchmarkEmbedding(marked, read.Valid && read.Payload.AsSpan().SequenceEqual(payloadBytes.Span),
            read.Payload, read.Confidence, rawBer, new DctReadContext(frame));
    }, cancellationToken);

    public Task<WatermarkBenchmarkRead> ReadAsync(PixelImage image, WatermarkBenchmarkEmbedding baseline,
        ReadOnlyMemory<byte> expectedPayload,
        CancellationToken cancellationToken) => Task.Run(() =>
    {
        if (baseline.ReadContext is not DctReadContext context)
            throw new ArgumentException("DCT benchmark 收到不属于自己的读取上下文。", nameof(baseline));
        try
        {
            var read = ReadCore(image, cancellationToken);
            return new WatermarkBenchmarkRead(read.Valid && read.Payload.AsSpan().SequenceEqual(expectedPayload.Span),
                read.Confidence, MeasureRawBer(image, context.Frame, cancellationToken));
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException)
        {
            double? rawBer = null;
            try { rawBer = MeasureRawBer(image, context.Frame, cancellationToken); }
            catch (Exception nested) when (nested is InvalidDataException or ArgumentException) { }
            return new(false, 0d, rawBer);
        }
    }, cancellationToken);

    private (bool Valid, byte[] Payload, double Confidence) ReadCore(PixelImage image, CancellationToken token)
    {
        var header = carrier.ReadHeader(image, token);
        var data = carrier.ReadData(image, header.Header, frameProtocol.ResolveMappingKey(header.Header, password: null), token);
        using var decoded = frameProtocol.DecodeData(header.Header, data.EncodedData, password: null).Payload;
        return (true, decoded.Bytes.ToArray(), (header.Confidence + data.Confidence) * 0.5d);
    }

    private double MeasureRawBer(PixelImage image, EncodedWatermarkFrame frame, CancellationToken token)
    {
        var header = carrier.ReadHeaderChannel(image, token);
        var data = carrier.ReadDataChannel(image, frame.Header, frame.MappingKey, token);
        var headerBer = ChannelBerCalculator.Compare(header.PhysicalBits, header.VotedBytes, frame.EncodedHeader,
            FrequencyWatermarkCarrier.HeaderRedundancy).Physical;
        var dataBer = ChannelBerCalculator.Compare(data.PhysicalBits, data.VotedBytes, frame.EncodedData,
            EmbeddingProfile.Resolve(frame.Header.Profile).DataRedundancy).Physical;
        var compared = headerBer.ComparedBits + dataBer.ComparedBits;
        return compared == 0 ? 0d : (headerBer.ErrorBits + dataBer.ErrorBits) / (double)compared;
    }

    private sealed record DctReadContext(EncodedWatermarkFrame Frame) : IWatermarkBenchmarkReadContext;
}

/// <summary>把实验性 Haar 系数对载体适配到相同 benchmark contract；强度量纲在报告中保持独立。</summary>
internal sealed class DwtWatermarkBenchmarkAdapter(DwtWatermarkCarrier carrier) : IWatermarkBenchmarkCarrier
{
    private const int Levels = 2;
    private const double Step = 32d;
    private const int Seed = 20260831;
    public string CarrierId => DwtWatermarkCarrier.CarrierId;

    public WatermarkBenchmarkCapacity Estimate(PixelImage source, int payloadLength) =>
        new(CarrierId, carrier.Estimate(source, Levels, payloadLength).MaximumPayloadBytes);

    public Task<WatermarkBenchmarkEmbedding> EmbedAndReadAsync(PixelImage source, ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken) => Task.Run(() =>
    {
        var embedded = carrier.Embed(source, payload.Span, Levels, Step, Seed, cancellationToken);
        var read = carrier.Read(embedded.Image, Levels, Step, Seed, cancellationToken);
        return new WatermarkBenchmarkEmbedding(embedded.Image,
            read.IntegrityValid && read.Payload.AsSpan().SequenceEqual(payload.Span), read.Payload, read.Confidence,
            carrier.MeasureRawBitErrorRate(embedded.Image, payload.Span, Levels, Step, Seed, cancellationToken),
            new DwtReadContext());
    }, cancellationToken);

    public Task<WatermarkBenchmarkRead> ReadAsync(PixelImage image, WatermarkBenchmarkEmbedding baseline,
        ReadOnlyMemory<byte> expectedPayload,
        CancellationToken cancellationToken) => Task.Run(() =>
    {
        if (baseline.ReadContext is not DwtReadContext)
            throw new ArgumentException("DWT benchmark 收到不属于自己的读取上下文。", nameof(baseline));
        var read = carrier.Read(image, Levels, Step, Seed, cancellationToken);
        double? rawBer = null;
        try { rawBer = carrier.MeasureRawBitErrorRate(image, expectedPayload.Span, Levels, Step, Seed, cancellationToken); }
        catch (InvalidDataException) { }
        return new WatermarkBenchmarkRead(
            read.IntegrityValid && read.Payload.AsSpan().SequenceEqual(expectedPayload.Span), read.Confidence, rawBer);
    }, cancellationToken);

    private sealed record DwtReadContext : IWatermarkBenchmarkReadContext;
}
