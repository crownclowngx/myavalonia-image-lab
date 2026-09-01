using System.Diagnostics;
using System.Security.Cryptography;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.Frequency;
using ImageLabPlugin.Domain.FrequencyFiltering;
using ImageLabPlugin.Domain.Imaging;
using ImageLabPlugin.Domain.SpectralArt;

namespace ImageLabPlugin.Application.SpectralArt;

/// <summary>显式解码一次载体，并在任何完整 FFT 大数组分配前执行 2048² 预算检查。</summary>
internal sealed class PrepareSpectralArtCarrierUseCase(
    IImageCodec codec,
    ImageChannelConverter channelConverter,
    FrequencySpectrumBuilder spectrumBuilder,
    SpectrumProjector spectrumProjector) : IPrepareSpectralArtCarrierUseCase
{
    public async Task<SpectralArtSession> ExecuteAsync(
        SpectralCarrierRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourcePath);
        var image = await codec.DecodeAsync(request.SourcePath, cancellationToken).ConfigureAwait(false);
        // 任一原始边超过 2048 时，下一次 radix-2 补零必然越界；先给出产品级错误，避免底层参数异常泄漏。
        if (image.Size.Width > 2048 || image.Size.Height > 2048)
            throw new InvalidOperationException("载体补零后超过共享 2048×2048 FFT 预算。");
        var paddedWidth = FrequencySpectrum.NextPowerOfTwo(image.Size.Width);
        var paddedHeight = FrequencySpectrum.NextPowerOfTwo(image.Size.Height);
        if (checked((long)paddedWidth * paddedHeight) > FrequencySpectrum.MaximumComplexValues)
            throw new InvalidOperationException("载体补零后超过共享 2048×2048 FFT 预算。");
        return await Task.Run(() =>
        {
            var luma = channelConverter.Extract(image, ImageChannel.Luma, cancellationToken);
            var spectrum = spectrumBuilder.Build(luma, cancellationToken);
            var preview = spectrumProjector.CreateMagnitude(spectrum,
                SpectrumMagnitudeMode.Logarithmic, cancellationToken);
            var sourceFingerprint = FingerprintImage(image);
            return new SpectralArtSession(request.SourcePath, image, luma, spectrum, preview, sourceFingerprint);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static string FingerprintImage(PixelImage image)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(BitConverter.GetBytes(image.Size.Width));
        hash.AppendData(BitConverter.GetBytes(image.Size.Height));
        hash.AppendData(image.Rgba.Span);
        return Convert.ToHexString(hash.GetHashAndReset())[..16].ToLowerInvariant();
    }
}

/// <summary>协调文字平台端口或图片解码端口，并统一进入领域 Pattern 规范化。</summary>
internal sealed class CreateSpectralPatternUseCase(
    IImageCodec codec,
    ISpectralTextRasterizer textRasterizer,
    SpectralPatternNormalizer normalizer) : ICreateSpectralPatternUseCase
{
    public async Task<SpectralPattern> ExecuteAsync(
        SpectralPatternRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        PixelImage source;
        if (request.SourceKind == SpectralPatternSourceKind.Text)
        {
            if (string.IsNullOrWhiteSpace(request.Text))
                throw new InvalidDataException("文字 Pattern 不能为空或只包含空白。");
            source = await textRasterizer.RasterizeAsync(new SpectralTextRasterRequest(request.Text,
                request.FontFamily, request.FontSize, request.FontWeight, request.Padding,
                SpectralPattern.MaximumEdge), cancellationToken).ConfigureAwait(false);
        }
        else
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(request.ImagePath);
            source = await codec.DecodeAsync(request.ImagePath, cancellationToken).ConfigureAwait(false);
        }
        return await Task.Run(() => normalizer.Normalize(source, request.Normalization, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>按“映射→单工作副本写入→频谱诊断→IFFT→空间诊断”的固定顺序提交完整结果。</summary>
internal sealed class RenderSpectralArtUseCase(
    SpectralPatternMapper mapper,
    SpectralAmplitudeWriter writer,
    SpectralArtReconstructor reconstructor,
    SpectralArtDiagnostics diagnostics,
    SpectrumProjector spectrumProjector,
    SpectralPatternPreviewProjector patternProjector) : IRenderSpectralArtUseCase
{
    public Task<SpectralArtResult> ExecuteAsync(
        SpectralArtSession session,
        SpectralArtRecipe recipe,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(recipe);
        session.ThrowIfDisposed();
        return Task.Run(() => ExecuteCore(session, recipe, cancellationToken), cancellationToken);
    }

    private SpectralArtResult ExecuteCore(
        SpectralArtSession session,
        SpectralArtRecipe recipe,
        CancellationToken cancellationToken)
    {
        session.ThrowIfDisposed();
        var watch = Stopwatch.StartNew();
        var mapping = mapper.Map(recipe.Pattern, recipe.Region, recipe.FitMode,
            session.Spectrum.PaddedWidth, session.Spectrum.PaddedHeight, cancellationToken);
        var mappingElapsed = watch.Elapsed;

        // 强度 0 必须在 CreateWorkingCopy 和 IFFT 之前短路；预览只复制普通 RGBA，绝不分配完整 Complex[]。
        if (recipe.Strength == 0d)
            return RenderNoOp(session, recipe, mapping, mappingElapsed, watch, cancellationToken);

        watch.Restart();
        var working = session.Spectrum.CreateWorkingCopy();
        var write = writer.ApplyInPlace(session.Spectrum, working, mapping, recipe.Strength, cancellationToken);
        var frequency = diagnostics.AnalyzeFrequency(session.Spectrum, working, mapping, write, cancellationToken);
        var scale = spectrumProjector.CreateSharedScale(session.Spectrum, working,
            SpectrumMagnitudeMode.Logarithmic);
        var sourceSpectrumPreview = spectrumProjector.CreateMagnitude(session.Spectrum, scale, cancellationToken);
        var resultSpectrumPreview = spectrumProjector.CreateMagnitude(session.Spectrum, working, scale,
            cancellationToken);
        var mappingPreview = diagnostics.CreateMappingPreview(mapping, cancellationToken);
        var spectrumDifference = diagnostics.CreateSpectrumDifference(session.Spectrum, working, cancellationToken);
        var frequencyElapsed = watch.Elapsed;

        watch.Restart();
        var reconstruction = reconstructor.Reconstruct(session.SourceImage, session.Spectrum, working, cancellationToken);
        var inverseElapsed = watch.Elapsed;

        watch.Restart();
        var quality = diagnostics.AnalyzeQuality(session.SourceImage, reconstruction.Image, cancellationToken);
        var difference2 = diagnostics.CreateSpatialDifference(session.LumaPlane,
            reconstruction.LumaPlane, 2d, cancellationToken);
        var difference4 = diagnostics.CreateSpatialDifference(session.LumaPlane,
            reconstruction.LumaPlane, 4d, cancellationToken);
        var difference8 = diagnostics.CreateSpatialDifference(session.LumaPlane,
            reconstruction.LumaPlane, 8d, cancellationToken);
        var spatialElapsed = watch.Elapsed;
        return new SpectralArtResult(session.SessionFingerprint, session.SourceFingerprint,
            recipe.Pattern.Fingerprint, recipe.Fingerprint(), mapping.Fingerprint, reconstruction.Image,
            patternProjector.Project(recipe.Pattern, cancellationToken), mappingPreview, sourceSpectrumPreview, resultSpectrumPreview,
            spectrumDifference, difference2, difference4, difference8, quality,
            reconstruction.RawStatistics, frequency,
            new SpectralArtTimings(mappingElapsed, frequencyElapsed, inverseElapsed, spatialElapsed));
    }

    private SpectralArtResult RenderNoOp(SpectralArtSession session, SpectralArtRecipe recipe,
        SpectralPatternMapping mapping, TimeSpan mappingElapsed, Stopwatch watch,
        CancellationToken cancellationToken)
    {
        watch.Restart();
        var frequency = diagnostics.AnalyzeNoOp(session.Spectrum, cancellationToken);
        var mappingPreview = diagnostics.CreateMappingPreview(mapping, cancellationToken);
        var sourceSpectrum = session.SourceSpectrumPreview.Clone();
        var resultSpectrum = session.SourceSpectrumPreview.Clone();
        var spectrumDifference = SpectralArtDiagnostics.CreateZeroSpectrumDifference(session.Spectrum, cancellationToken);
        var frequencyElapsed = watch.Elapsed;

        watch.Restart();
        var output = session.SourceImage.Clone();
        var luma = session.LumaPlane;
        var values = luma.Values.Span;
        var minimum = values.Length == 0 ? 0d : values[0];
        var maximum = minimum;
        for (var i = 1; i < values.Length; i++)
        {
            if ((i & 16383) == 0) cancellationToken.ThrowIfCancellationRequested();
            minimum = Math.Min(minimum, values[i]); maximum = Math.Max(maximum, values[i]);
        }
        var raw = new SpectralRawStatistics(minimum, maximum, 0, 0, 0, 0, 0d);
        var inverseElapsed = watch.Elapsed;

        watch.Restart();
        var quality = diagnostics.AnalyzeQuality(session.SourceImage, output, cancellationToken);
        var difference2 = diagnostics.CreateSpatialDifference(luma, luma, 2d, cancellationToken);
        var difference4 = diagnostics.CreateSpatialDifference(luma, luma, 4d, cancellationToken);
        var difference8 = diagnostics.CreateSpatialDifference(luma, luma, 8d, cancellationToken);
        var spatialElapsed = watch.Elapsed;
        return new SpectralArtResult(session.SessionFingerprint, session.SourceFingerprint,
            recipe.Pattern.Fingerprint, recipe.Fingerprint(), mapping.Fingerprint, output,
            patternProjector.Project(recipe.Pattern, cancellationToken), mappingPreview, sourceSpectrum,
            resultSpectrum, spectrumDifference, difference2, difference4, difference8, quality, raw,
            frequency, new SpectralArtTimings(mappingElapsed, frequencyElapsed, inverseElapsed, spatialElapsed));
    }

}

internal sealed class ExportSpectralArtImageUseCase(IImageCodec codec, IAtomicFileWriter writer,
    SpectralExportFactVerifier factVerifier)
    : IExportSpectralArtImageUseCase
{
    public async Task ExecuteAsync(SpectralArtSession session, SpectralArtResult result,
        SpectralArtRecipe expectedRecipe, string outputPath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(expectedRecipe);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        session.ThrowIfDisposed();
        if (!StringComparer.Ordinal.Equals(result.SessionFingerprint, session.SessionFingerprint) ||
            !StringComparer.Ordinal.Equals(result.RecipeFingerprint, expectedRecipe.Fingerprint()))
            throw new InvalidOperationException("结果已过期，禁止导出。");
        if (Path.GetFullPath(outputPath).Equals(Path.GetFullPath(session.SourcePath),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Spectral Art 不允许覆盖载体源文件。");
        var bytes = await codec.EncodeAsync(result.Output, ImageOutputFormat.Png, 100, cancellationToken)
            .ConfigureAwait(false);
        var decoded = await codec.DecodeAsync(bytes, cancellationToken).ConfigureAwait(false);
        if (decoded.Size != result.Output.Size || !decoded.Rgba.Span.SequenceEqual(result.Output.Rgba.Span))
            throw new InvalidOperationException("PNG 无损 RGBA 回读不一致，文件未发布。");
        var facts = factVerifier.Verify(decoded, expectedRecipe.Pattern, expectedRecipe.Region,
            expectedRecipe.FitMode, result.MappingFingerprint, cancellationToken);
        if (facts.PaddedWidth != session.Spectrum.PaddedWidth || facts.PaddedHeight != session.Spectrum.PaddedHeight)
            throw new InvalidOperationException("PNG 回读频谱补零尺寸不一致，文件未发布。");
        await writer.WriteAsync(outputPath, bytes, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class ImportSpectralArtRecipeUseCase(ITextFileReader reader, ISpectralArtRecipeSerializer serializer)
    : IImportSpectralArtRecipeUseCase
{
    public const int MaximumJsonBytes = 4 * 1024 * 1024;
    public async Task<SpectralArtRecipe> ExecuteAsync(string path, CancellationToken cancellationToken)
    {
        var bytes = await reader.ReadAsync(path, MaximumJsonBytes, cancellationToken).ConfigureAwait(false);
        return serializer.Deserialize(bytes);
    }
}

internal sealed class ExportSpectralArtRecipeUseCase(ISpectralArtRecipeSerializer serializer, IAtomicFileWriter writer)
    : IExportSpectralArtRecipeUseCase
{
    public Task ExecuteAsync(SpectralArtRecipe recipe, string path, CancellationToken cancellationToken)
    {
        var bytes = serializer.Serialize(recipe);
        if (bytes.Length > ImportSpectralArtRecipeUseCase.MaximumJsonBytes)
            throw new InvalidDataException("Spectral Art 配方超过 4 MiB 上限。");
        return writer.WriteAsync(path, bytes, cancellationToken);
    }
}

internal sealed class ExportSpectralArtReportUseCase(ISpectralArtReportSerializer serializer,
    IAtomicFileWriter writer) : IExportSpectralArtReportUseCase
{
    public Task ExecuteAsync(SpectralArtReport report, string path, bool csv,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        var bytes = csv ? serializer.SerializeCsv(report) : serializer.SerializeJson(report);
        if (bytes.Length > 1024 * 1024) throw new InvalidDataException("Spectral Art 报告超过 1 MiB 上限。");
        return writer.WriteAsync(path, bytes, cancellationToken);
    }
}
