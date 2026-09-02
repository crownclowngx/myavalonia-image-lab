using System.Numerics;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Application.SpectrumAnalysis;
using ImageLabPlugin.Domain.Shared.Spectral;
using ImageLabPlugin.Domain.Shared.Imaging;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class SpectrumUseCaseTests
{
    [Fact]
    public async Task 分析用例建立代理频谱三种预览与能量会话()
    {
        var image = CreateGradient(16, 8);
        var useCase = CreateAnalyzeUseCase(image);

        var analysis = await useCase.ExecuteAsync(new SpectrumAnalysisRequest("memory.png", ImageChannel.Luma, 512), CancellationToken.None);
        using var result = analysis.Session;

        Assert.Equal(image.Size, result.SourceImage.Size);
        Assert.Equal(image.Size, result.ProxyImage.Size);
        Assert.Equal((16, 8), (result.Spectrum.PaddedWidth, result.Spectrum.PaddedHeight));
        Assert.Equal(256, result.RadialEnergy.Bins.Count);
    }

    [Fact]
    public async Task 全通重建逐字节一致且Alpha保持()
    {
        var analysis = await CreateAnalyzeUseCase(CreateGradient(8, 8)).ExecuteAsync(
            new SpectrumAnalysisRequest("memory.png", ImageChannel.ChromaBlue, 512), CancellationToken.None);
        using var session = analysis.Session;
        var useCase = CreateReconstructUseCase();

        var result = await useCase.ExecuteAsync(session,
            new FrequencyBandDefinition(FrequencyBandKind.All, FrequencyBandBoundaries.Default), CancellationToken.None);

        Assert.True(result.UsedExactAllPassShortcut);
        Assert.Equal(session.ProxyImage.Rgba.ToArray(), result.Image.Rgba.ToArray());
    }

    [Fact]
    public async Task 仅Dc重建得到选定通道常量且虚部满足门禁()
    {
        var source = CreateGradient(8, 8);
        var analysis = await CreateAnalyzeUseCase(source).ExecuteAsync(
            new SpectrumAnalysisRequest("memory.png", ImageChannel.Red, 512), CancellationToken.None);
        using var session = analysis.Session;
        var result = await CreateReconstructUseCase().ExecuteAsync(session,
            new FrequencyBandDefinition(FrequencyBandKind.Custom, FrequencyBandBoundaries.Default, 0d, 0.01d), CancellationToken.None);

        var reds = Enumerable.Range(0, 64).Select(i => result.Image.Rgba.Span[i * 4]).Distinct().ToArray();
        Assert.Single(reds);
        Assert.True(result.MaximumImaginaryResidual < 1e-8);
        for (var i = 0; i < 64; i++) Assert.Equal(source.Rgba.Span[(i * 4) + 3], result.Image.Rgba.Span[(i * 4) + 3]);
    }

    [Fact]
    public void 块检查拒绝非完整边缘且完整块误差很小()
    {
        var analyzer = new DctBlockAnalyzer(new ImageChannelConverter(), new Dct8x8Transform());
        var image = CreateGradient(10, 10);
        Assert.True(analyzer.Analyze(image, ImageChannel.Luma, new ImagePoint(2, 2)).IsAvailable);
        var edge = analyzer.Analyze(image, ImageChannel.Luma, new ImagePoint(9, 9));
        Assert.False(edge.IsAvailable); Assert.Contains("非完整", edge.UnavailableReason, StringComparison.Ordinal);
    }

    private static AnalyzeSpectrumUseCase CreateAnalyzeUseCase(PixelImage image)
    {
        var channel = new ImageChannelConverter(); var dct = new Dct8x8Transform(); var one = new Fft1DTransform();
        return new AnalyzeSpectrumUseCase(new MemoryCodec(image), new ImageAnalysisProxyProjector(), channel,
            new Fft2DTransform(one), new SpectrumProjector(), new DctSpectrumProjector(channel, dct), new RadialEnergyAnalyzer());
    }

    private static ReconstructSpectrumBandUseCase CreateReconstructUseCase() =>
        new(new Fft2DTransform(new Fft1DTransform()), new FrequencyBandMaskFactory(), new ImageChannelConverter());

    private static PixelImage CreateGradient(int width, int height)
    {
        var rgba = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++) { rgba[i * 4] = (byte)(i * 3); rgba[(i * 4) + 1] = (byte)(255 - i * 2); rgba[(i * 4) + 2] = (byte)i; rgba[(i * 4) + 3] = (byte)(100 + i); }
        return new PixelImage(new ImageSize(width, height), rgba);
    }

    private sealed class MemoryCodec(PixelImage image) : IImageCodec
    {
        public Task<PixelImage> DecodeAsync(string path, CancellationToken cancellationToken) => Task.FromResult(image.Clone());
        public Task<PixelImage> DecodeAsync(ReadOnlyMemory<byte> encodedImage, CancellationToken cancellationToken) => Task.FromResult(image.Clone());
        public Task<byte[]> EncodeAsync(PixelImage value, ImageOutputFormat format, int jpegQuality, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
