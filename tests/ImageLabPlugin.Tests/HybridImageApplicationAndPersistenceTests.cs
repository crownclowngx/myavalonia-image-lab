using System.Buffers.Binary;
using System.Text;
using ImageLabPlugin.Application.HybridImage;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.HybridImage;
using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Infrastructure.Persistence;
using ImageLabPlugin.Plugin;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class HybridImageApplicationAndPersistenceTests
{
    [Fact]
    public async Task Prepare_DecodesEachInputOnceAndCreatesIndependentProxies()
    {
        var codec = new TestCodec(CreateGradient(80, 64), CreateGradient(64, 80));
        var provider = CreateProvider(codec);
        var useCase = provider.GetRequiredService<IPrepareHybridInputsUseCase>();

        using var session = await useCase.ExecuteAsync(new PrepareHybridInputsRequest("a.png", "b.png", 48), default);

        Assert.Equal(1, codec.PathDecodeCounts["a.png"]);
        Assert.Equal(1, codec.PathDecodeCounts["b.png"]);
        Assert.Equal(48, Math.Max(session.ProxyA.Size.Width, session.ProxyA.Size.Height));
        Assert.Equal(48, Math.Max(session.ProxyB.Size.Width, session.ProxyB.Size.Height));
        Assert.NotEqual(session.FingerprintA, session.FingerprintB);
    }

    [Fact]
    public async Task PreviewAndFullSize_UseDifferentOwnedSourcesAndCommitByGeneration()
    {
        var codec = new TestCodec(CreateGradient(80, 64), CreateGradient(80, 64));
        var provider = CreateProvider(codec);
        using var session = await provider.GetRequiredService<IPrepareHybridInputsUseCase>()
            .ExecuteAsync(new PrepareHybridInputsRequest("a.png", "b.png", 48), default);
        var points = IdentityPoints();
        await provider.GetRequiredService<ISolveHybridAlignmentUseCase>()
            .ExecuteAsync(session, new SolveHybridAlignmentRequest(points), default);
        var recipe = new HybridImageRecipe(points, new HybridNormalizedCrop(.05, .05, .95, .9), .8, .8);

        var previewGeneration = session.AdvanceGeneration();
        var preview = await provider.GetRequiredService<IRenderHybridPreviewUseCase>()
            .ExecuteAsync(session, recipe, previewGeneration, default);
        Assert.True(session.TryCommit(preview, previewGeneration, recipe.Fingerprint()));
        Assert.False(preview.IsFullSize);
        Assert.True(preview.Cutoff.LowCyclesPerImage > 0d);
        Assert.True(preview.Cutoff.LowDisplayRadiusPixels > 0d);
        Assert.Contains("理论", preview.Cutoff.Explanation, StringComparison.Ordinal);
        var sourceSpectrum = preview.Spectra.Project(HybridSpectrumKind.SourceA);
        var rawSpectrum = preview.Spectra.Project(HybridSpectrumKind.Raw);
        Assert.Equal(sourceSpectrum.Size, rawSpectrum.Size);
        Assert.Equal(preview.Spectra.PaddedWidth, rawSpectrum.Size.Width);

        var fullGeneration = session.AdvanceGeneration();
        var full = await provider.GetRequiredService<IRenderHybridFullSizeUseCase>()
            .ExecuteAsync(session, recipe, fullGeneration, default);
        Assert.True(session.TryCommit(full, fullGeneration, recipe.Fingerprint()));
        Assert.True(full.IsFullSize);
        Assert.True(full.Composition.Quantized.Size.Width > preview.Composition.Quantized.Size.Width);
        Assert.Same(full, session.LastFullSize);
    }

    [Fact]
    public async Task LateCandidate_IsRejectedAndLastValidResultIsPreserved()
    {
        var provider = CreateProvider(new TestCodec(CreateGradient(64, 64), CreateGradient(64, 64)));
        using var session = await provider.GetRequiredService<IPrepareHybridInputsUseCase>()
            .ExecuteAsync(new PrepareHybridInputsRequest("a.png", "b.png", 64), default);
        var recipe = new HybridImageRecipe(IdentityPoints(), new HybridNormalizedCrop(.1, .1, .8, .8), .8, .8);
        var generation = session.AdvanceGeneration();
        var candidate = await provider.GetRequiredService<IRenderHybridPreviewUseCase>()
            .ExecuteAsync(session, recipe, generation, default);
        Assert.True(session.TryCommit(candidate, generation, recipe.Fingerprint()));

        session.AdvanceGeneration();
        Assert.False(session.TryCommit(candidate, generation, recipe.Fingerprint()));
        Assert.Same(candidate, session.LastPreview);
    }

    [Fact]
    public void ResourceEstimator_RejectsBeforeLargeAllocation()
    {
        var estimator = new HybridResourceEstimator();
        Assert.Throws<InvalidOperationException>(() => estimator.EnsureWithinBudget(
            new ImageSize(4000, 4000), 32d, 32d));
    }

    [Fact]
    public void RecipeSerializer_StrictRoundTripAndUnknownFieldRejection()
    {
        var serializer = new HybridImageRecipeSerializer();
        var recipe = new HybridImageRecipe(IdentityPoints(), new HybridNormalizedCrop(.1, .2, .8, .9));
        var bytes = serializer.Serialize(recipe, new string('a', 24), new string('b', 24));
        var restored = serializer.Deserialize(bytes, out var fingerprintA, out var fingerprintB);
        Assert.Equal(recipe.Fingerprint(), restored.Fingerprint());
        Assert.Equal(new string('a', 24), fingerprintA);
        Assert.Equal(new string('b', 24), fingerprintB);

        var text = Encoding.UTF8.GetString(bytes).Replace("\"schema\": 1,", "\"schema\": 1,\n  \"unknown\": true,", StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => serializer.Deserialize(Encoding.UTF8.GetBytes(text), out _, out _));
    }

    [Fact]
    public void RecipeSerializer_RejectsDuplicateAndFingerprintTampering()
    {
        var serializer = new HybridImageRecipeSerializer();
        var recipe = new HybridImageRecipe(IdentityPoints(), new HybridNormalizedCrop(.1, .1, .8, .8));
        var text = Encoding.UTF8.GetString(serializer.Serialize(recipe, new string('a', 24), new string('b', 24)));
        var duplicate = text.Replace("\"schema\": 1,", "\"schema\": 1,\n  \"schema\": 1,", StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => serializer.Deserialize(Encoding.UTF8.GetBytes(duplicate), out _, out _));
        var tampered = text.Replace("\"lowGain\": 1", "\"lowGain\": 1.5", StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => serializer.Deserialize(Encoding.UTF8.GetBytes(tampered), out _, out _));
    }

    [Fact]
    public async Task Export_RequiresCurrentFullResultAndVerifiesRealTarget()
    {
        var codec = new TestCodec(CreateGradient(64, 64), CreateGradient(64, 64));
        var provider = CreateProvider(codec);
        using var session = await provider.GetRequiredService<IPrepareHybridInputsUseCase>()
            .ExecuteAsync(new PrepareHybridInputsRequest("a.png", "b.png", 64), default);
        var recipe = new HybridImageRecipe(IdentityPoints(), new HybridNormalizedCrop(.1, .1, .8, .8), .8, .8);
        var generation = session.AdvanceGeneration();
        var full = await provider.GetRequiredService<IRenderHybridFullSizeUseCase>()
            .ExecuteAsync(session, recipe, generation, default);
        Assert.True(session.TryCommit(full, generation, recipe.Fingerprint()));
        var path = Path.Combine(Path.GetTempPath(), $"hybrid-{Guid.NewGuid():N}.png");
        try
        {
            await provider.GetRequiredService<IExportHybridImageUseCase>()
                .ExecuteAsync(session, full, recipe, path, default);
            Assert.True(File.Exists(path));
            Assert.Equal(full.Composition.Quantized.Rgba.ToArray(), (await codec.DecodeAsync(path, default)).Rgba.ToArray());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Export_RejectsInputOverwriteAndPreview()
    {
        var codec = new TestCodec(CreateGradient(64, 64), CreateGradient(64, 64));
        var provider = CreateProvider(codec);
        using var session = await provider.GetRequiredService<IPrepareHybridInputsUseCase>()
            .ExecuteAsync(new PrepareHybridInputsRequest("a.png", "b.png", 64), default);
        var recipe = new HybridImageRecipe(IdentityPoints(), new HybridNormalizedCrop(.1, .1, .8, .8), .8, .8);
        var generation = session.AdvanceGeneration();
        var preview = await provider.GetRequiredService<IRenderHybridPreviewUseCase>()
            .ExecuteAsync(session, recipe, generation, default);
        Assert.True(session.TryCommit(preview, generation, recipe.Fingerprint()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetRequiredService<IExportHybridImageUseCase>()
            .ExecuteAsync(session, preview, recipe, "a.png", default));
    }

    private static ServiceProvider CreateProvider(TestCodec codec)
    {
        var services = new ServiceCollection();
        services.AddImageLabPluginServices();
        services.AddSingleton<IImageCodec>(codec);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static HybridAlignmentPointPair[] IdentityPoints() =>
    [
        new(1, new HybridNormalizedPoint(.1, .1), new HybridNormalizedPoint(.1, .1)),
        new(2, new HybridNormalizedPoint(.8, .1), new HybridNormalizedPoint(.8, .1)),
        new(3, new HybridNormalizedPoint(.1, .8), new HybridNormalizedPoint(.1, .8))
    ];

    private static PixelImage CreateGradient(int width, int height)
    {
        var bytes = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++)
        {
            bytes[i * 4] = (byte)(i % 251);
            bytes[(i * 4) + 1] = (byte)((i * 3) % 251);
            bytes[(i * 4) + 2] = (byte)((i * 7) % 251);
            bytes[(i * 4) + 3] = 255;
        }
        return new PixelImage(new ImageSize(width, height), bytes);
    }

    private sealed class TestCodec(PixelImage a, PixelImage b) : IImageCodec
    {
        public Dictionary<string, int> PathDecodeCounts { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<PixelImage> DecodeAsync(string path, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PathDecodeCounts[path] = PathDecodeCounts.GetValueOrDefault(path) + 1;
            if (File.Exists(path)) return DecodeAsync(File.ReadAllBytes(path), cancellationToken);
            return Task.FromResult((path.Contains('a', StringComparison.OrdinalIgnoreCase) ? a : b).Clone());
        }

        public Task<PixelImage> DecodeAsync(ReadOnlyMemory<byte> encodedImage, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var span = encodedImage.Span;
            var width = BinaryPrimitives.ReadInt32LittleEndian(span);
            var height = BinaryPrimitives.ReadInt32LittleEndian(span[4..]);
            return Task.FromResult(new PixelImage(new ImageSize(width, height), span[8..]));
        }

        public Task<byte[]> EncodeAsync(PixelImage image, ImageOutputFormat format, int jpegQuality,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(ImageOutputFormat.Png, format);
            var bytes = new byte[8 + image.Rgba.Length];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, image.Size.Width);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), image.Size.Height);
            image.Rgba.Span.CopyTo(bytes.AsSpan(8));
            return Task.FromResult(bytes);
        }
    }
}
