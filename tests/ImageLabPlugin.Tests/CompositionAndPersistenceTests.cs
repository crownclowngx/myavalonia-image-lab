using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ImageLabPlugin.Constants;
using ImageLabPlugin.Features.WatermarkEmbed;
using ImageLabPlugin.Features.WatermarkInspect;
using ImageLabPlugin.Features.SpectrumInspector;
using ImageLabPlugin.Features.ImageCompareLab;
using ImageLabPlugin.Features.RobustnessLab;
using ImageLabPlugin.Features.ImageFingerprint;
using ImageLabPlugin.Features.BitPlaneViewer;
using ImageLabPlugin.Features.LsbSteganographyLab;
using ImageLabPlugin.Features.ConvolutionPlayground;
using ImageLabPlugin.Features.WaveletLab;
using ImageLabPlugin.Features.FrequencyFilter;
using ImageLabPlugin.Features.FrequencyMaskEditor;
using ImageLabPlugin.Features.PeriodicNoiseRemoval;
using ImageLabPlugin.Features.SvdDecomposition;
using ImageLabPlugin.Features.PaletteColorTransfer;
using ImageLabPlugin.Features.SeamCarving;
using ImageLabPlugin.Domain.SeamCarving;
using ImageLabPlugin.Features.PoissonBlending;
using ImageLabPlugin.Features.SpectralArt;
using ImageLabPlugin.Domain.SpectralArt;
using ImageLabPlugin.Domain.PoissonBlending;
using ImageLabPlugin.Features.HybridImage;
using ImageLabPlugin.Domain.HybridImage;
using ImageLabPlugin.Features.MagnitudePhaseSwap;
using ImageLabPlugin.Features.ImageOscilloscope;
using ImageLabPlugin.Domain.FrequencyFiltering;
using ImageLabPlugin.Infrastructure.Persistence;
using ImageLabPlugin.Plugin;
using ImageLabPlugin.Domain.Frequency;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;
using Xunit;

namespace ImageLabPlugin.Tests;

/// <summary>覆盖插件贡献、Scoped Document 隔离和原子文件发布。</summary>
public sealed class CompositionAndPersistenceTests
{
    [Fact]
    public void Module只贡献二十一个稳定的PersistableDocument且不贡献Tool()
    {
        var registration = new RecordingRegistration();

        new ImageLabPluginModule().Configure(registration);

        Assert.Equal(
            new[] { PluginIds.WatermarkEmbedDocument, PluginIds.WatermarkInspectDocument, PluginIds.SpectrumInspectorDocument, PluginIds.ImageCompareLabDocument, PluginIds.RobustnessLabDocument, PluginIds.ImageFingerprintDocument, PluginIds.BitPlaneViewerDocument, PluginIds.LsbSteganographyLabDocument, PluginIds.ConvolutionPlaygroundDocument, PluginIds.WaveletLabDocument, PluginIds.FrequencyFilterDocument, PluginIds.FrequencyMaskEditorDocument, PluginIds.PeriodicNoiseRemovalDocument, PluginIds.SvdDecompositionDocument, PluginIds.PaletteColorTransferDocument, PluginIds.SeamCarvingDocument, PluginIds.PoissonBlendingDocument, PluginIds.SpectralArtDocument, PluginIds.HybridImageDocument, PluginIds.MagnitudePhaseSwapDocument, PluginIds.ImageOscilloscopeDocument },
            registration.PersistableDocumentIds);
        Assert.Empty(registration.DocumentIds);
        Assert.Empty(registration.ToolIds);
    }

    [Fact]
    public void 两个Scope解析出的Document实例彼此隔离并共享无状态算法服务()
    {
        var registration = new RecordingRegistration();
        registration.Services.AddSingleton<IPluginWindowInteraction, NullWindowInteraction>();
        registration.Services.AddScoped<IDocumentLifetime, TestLifetime>();
        new ImageLabPluginModule().Configure(registration);
        using var provider = registration.Services.BuildServiceProvider(validateScopes: true);
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        var first = firstScope.ServiceProvider.GetRequiredService<WatermarkEmbedDocument>();
        var second = secondScope.ServiceProvider.GetRequiredService<WatermarkEmbedDocument>();
        var inspect = firstScope.ServiceProvider.GetRequiredService<WatermarkInspectDocument>();
        var spectrum = firstScope.ServiceProvider.GetRequiredService<SpectrumInspectorDocument>();
        var secondSpectrum = secondScope.ServiceProvider.GetRequiredService<SpectrumInspectorDocument>();
        var compare = firstScope.ServiceProvider.GetRequiredService<ImageCompareLabDocument>();
        var secondCompare = secondScope.ServiceProvider.GetRequiredService<ImageCompareLabDocument>();
        var robustness = firstScope.ServiceProvider.GetRequiredService<RobustnessLabDocument>();
        var secondRobustness = secondScope.ServiceProvider.GetRequiredService<RobustnessLabDocument>();
        var fingerprint = firstScope.ServiceProvider.GetRequiredService<ImageFingerprintDocument>();
        var secondFingerprint = secondScope.ServiceProvider.GetRequiredService<ImageFingerprintDocument>();
        var bitPlane = firstScope.ServiceProvider.GetRequiredService<BitPlaneViewerDocument>();
        var secondBitPlane = secondScope.ServiceProvider.GetRequiredService<BitPlaneViewerDocument>();
        var lsb = firstScope.ServiceProvider.GetRequiredService<LsbSteganographyLabDocument>();
        var secondLsb = secondScope.ServiceProvider.GetRequiredService<LsbSteganographyLabDocument>();
        var convolution = firstScope.ServiceProvider.GetRequiredService<ConvolutionPlaygroundDocument>();
        var secondConvolution = secondScope.ServiceProvider.GetRequiredService<ConvolutionPlaygroundDocument>();
        var wavelet = firstScope.ServiceProvider.GetRequiredService<WaveletLabDocument>();
        var secondWavelet = secondScope.ServiceProvider.GetRequiredService<WaveletLabDocument>();
        var frequencyFilter = firstScope.ServiceProvider.GetRequiredService<FrequencyFilterDocument>();
        var secondFrequencyFilter = secondScope.ServiceProvider.GetRequiredService<FrequencyFilterDocument>();
        var frequencyMask = firstScope.ServiceProvider.GetRequiredService<FrequencyMaskEditorDocument>();
        var secondFrequencyMask = secondScope.ServiceProvider.GetRequiredService<FrequencyMaskEditorDocument>();
        var periodicNoise = firstScope.ServiceProvider.GetRequiredService<PeriodicNoiseRemovalDocument>();
        var secondPeriodicNoise = secondScope.ServiceProvider.GetRequiredService<PeriodicNoiseRemovalDocument>();
        var svd = firstScope.ServiceProvider.GetRequiredService<SvdDecompositionDocument>();
        var secondSvd = secondScope.ServiceProvider.GetRequiredService<SvdDecompositionDocument>();
        var paletteColor = firstScope.ServiceProvider.GetRequiredService<PaletteColorTransferDocument>();
        var secondPaletteColor = secondScope.ServiceProvider.GetRequiredService<PaletteColorTransferDocument>();
        var seamCarving = firstScope.ServiceProvider.GetRequiredService<SeamCarvingDocument>();
        var secondSeamCarving = secondScope.ServiceProvider.GetRequiredService<SeamCarvingDocument>();
        var poisson = firstScope.ServiceProvider.GetRequiredService<PoissonBlendingDocument>();
        var secondPoisson = secondScope.ServiceProvider.GetRequiredService<PoissonBlendingDocument>();
        var spectralArt = firstScope.ServiceProvider.GetRequiredService<SpectralArtDocument>();
        var secondSpectralArt = secondScope.ServiceProvider.GetRequiredService<SpectralArtDocument>();
        var hybridImage = firstScope.ServiceProvider.GetRequiredService<HybridImageDocument>();
        var secondHybridImage = secondScope.ServiceProvider.GetRequiredService<HybridImageDocument>();
        var magnitudePhase = firstScope.ServiceProvider.GetRequiredService<MagnitudePhaseSwapDocument>();
        var secondMagnitudePhase = secondScope.ServiceProvider.GetRequiredService<MagnitudePhaseSwapDocument>();
        var imageOscilloscope = firstScope.ServiceProvider.GetRequiredService<ImageOscilloscopeDocument>();
        var secondImageOscilloscope = secondScope.ServiceProvider.GetRequiredService<ImageOscilloscopeDocument>();

        Assert.NotSame(first, second);
        Assert.NotSame(first, inspect);
        Assert.NotSame(first, spectrum);
        Assert.NotSame(spectrum, secondSpectrum);
        Assert.NotSame(compare, secondCompare);
        Assert.NotSame(robustness, secondRobustness);
        Assert.NotSame(fingerprint, secondFingerprint);
        Assert.NotSame(bitPlane, secondBitPlane);
        Assert.NotSame(lsb, secondLsb);
        Assert.NotSame(convolution, secondConvolution);
        Assert.NotSame(wavelet, secondWavelet);
        Assert.NotSame(frequencyFilter, secondFrequencyFilter);
        Assert.NotSame(frequencyMask, secondFrequencyMask);
        Assert.NotSame(periodicNoise, secondPeriodicNoise);
        Assert.NotSame(svd, secondSvd);
        Assert.NotSame(paletteColor, secondPaletteColor);
        Assert.NotSame(seamCarving, secondSeamCarving);
        Assert.NotSame(poisson, secondPoisson);
        Assert.NotSame(spectralArt, secondSpectralArt);
        Assert.NotSame(hybridImage, secondHybridImage);
        Assert.NotSame(magnitudePhase, secondMagnitudePhase);
        Assert.NotSame(imageOscilloscope, secondImageOscilloscope);
        imageOscilloscope.SourcePath = "scope-oscilloscope-one";
        Assert.Empty(secondImageOscilloscope.SourcePath);
        magnitudePhase.PathA = "scope-magnitude-one";
        Assert.Empty(secondMagnitudePhase.PathA);
        hybridImage.PathA = "scope-hybrid-one";
        Assert.Empty(secondHybridImage.PathA);
        spectralArt.SourcePath = "scope-spectral-one";
        Assert.Empty(secondSpectralArt.SourcePath);
        poisson.SourcePath = "scope-poisson-one";
        Assert.Empty(secondPoisson.SourcePath);
        seamCarving.SourcePath = "scope-seam-one";
        Assert.Empty(secondSeamCarving.SourcePath);
        paletteColor.TargetPath = "scope-palette-one";
        Assert.Empty(secondPaletteColor.TargetPath);
        svd.SourcePath = "scope-svd-one";
        Assert.Empty(secondSvd.SourcePath);
        periodicNoise.SourcePath = "scope-periodic-one";
        Assert.Empty(secondPeriodicNoise.SourcePath);
        frequencyMask.SourcePath = "scope-frequency-mask-one";
        Assert.Empty(secondFrequencyMask.SourcePath);
        frequencyFilter.SourcePath = "scope-frequency-filter-one";
        Assert.Empty(secondFrequencyFilter.SourcePath);
        wavelet.SourcePath = "scope-wavelet-one";
        Assert.Empty(secondWavelet.SourcePath);
        convolution.SourcePath = "scope-convolution-one";
        convolution.KernelText = "0 0 0\n0 1 0\n0 0 0";
        Assert.Empty(secondConvolution.SourcePath);
        Assert.NotEqual(convolution.KernelText, secondConvolution.KernelText);
        lsb.SourcePath = "scope-lsb-one";
        lsb.SeedText = "42";
        Assert.Empty(secondLsb.SourcePath);
        Assert.Equal("1", secondLsb.SeedText);
        bitPlane.SourcePath = "scope-bit-plane-one";
        Assert.Empty(secondBitPlane.SourcePath);
        fingerprint.ReferencePath = "scope-fingerprint-one";
        Assert.Empty(secondFingerprint.ReferencePath);
        robustness.SourcePath = "scope-robustness-one";
        Assert.Empty(secondRobustness.SourcePath);
        compare.ReferencePath = "scope-compare-one";
        Assert.Empty(secondCompare.ReferencePath);
        spectrum.SourcePath = "scope-spectrum-one";
        Assert.Empty(secondSpectrum.SourcePath);
        Assert.Same(
            firstScope.ServiceProvider.GetRequiredService<Fft1DTransform>(),
            secondScope.ServiceProvider.GetRequiredService<Fft1DTransform>());
        Assert.Same(
            firstScope.ServiceProvider.GetRequiredService<RadialFilterResponse>(),
            secondScope.ServiceProvider.GetRequiredService<RadialFilterResponse>());
        Assert.Same(
            firstScope.ServiceProvider.GetRequiredService<SobelEnergyCalculator>(),
            secondScope.ServiceProvider.GetRequiredService<SobelEnergyCalculator>());
        Assert.Same(
            firstScope.ServiceProvider.GetRequiredService<PoissonRelaxationSolver>(),
            secondScope.ServiceProvider.GetRequiredService<PoissonRelaxationSolver>());
        Assert.Same(
            firstScope.ServiceProvider.GetRequiredService<SpectralPatternMapper>(),
            secondScope.ServiceProvider.GetRequiredService<SpectralPatternMapper>());
        Assert.Same(
            firstScope.ServiceProvider.GetRequiredService<GaussianPlaneFilter>(),
            secondScope.ServiceProvider.GetRequiredService<GaussianPlaneFilter>());
        first.PayloadText = "scope-one";
        Assert.Empty(second.PayloadText);
    }

    [Fact]
    public async Task 频域滤波快照只恢复轻量参数且不自动建立Session()
    {
        var registration = new RecordingRegistration();
        registration.Services.AddSingleton<IPluginWindowInteraction, NullWindowInteraction>();
        registration.Services.AddScoped<IDocumentLifetime, TestLifetime>();
        new ImageLabPluginModule().Configure(registration);
        using var provider = registration.Services.BuildServiceProvider(validateScopes: true);
        using var firstScope = provider.CreateScope();
        var document = firstScope.ServiceProvider.GetRequiredService<FrequencyFilterDocument>();
        await document.InitializeAsync(new NewDocumentActivation("频域滤波"), default);
        document.SourcePath = "missing.png"; document.SelectedKind = "带阻"; document.SelectedFamily = "Butterworth";
        document.InnerCutoff = 0.22; document.OuterCutoff = 0.71; document.ButterworthOrder = 6; document.KernelSize = 15;
        var snapshot = await document.CaptureSaveSnapshotAsync(default);

        using var secondScope = provider.CreateScope();
        var restored = secondScope.ServiceProvider.GetRequiredService<FrequencyFilterDocument>();
        await restored.InitializeAsync(new RestoreDocumentActivation("频域滤波", snapshot.Content), default);
        Assert.Equal("带阻", restored.SelectedKind); Assert.Equal("Butterworth", restored.SelectedFamily);
        Assert.Equal(0.22, restored.InnerCutoff); Assert.Equal(0.71, restored.OuterCutoff); Assert.Equal(6, restored.ButterworthOrder);
        Assert.Equal(15, restored.KernelSize); Assert.False(restored.HasSession); Assert.False(restored.HasResult); Assert.False(restored.IsDirty);
    }

    [Fact]
    public async Task 频谱遮罩快照保存有界配方但不保存大数组且恢复不自动FFT()
    {
        var registration = new RecordingRegistration();
        registration.Services.AddSingleton<IPluginWindowInteraction, NullWindowInteraction>();
        registration.Services.AddScoped<IDocumentLifetime, TestLifetime>();
        new ImageLabPluginModule().Configure(registration);
        using var provider = registration.Services.BuildServiceProvider(validateScopes: true);
        using var firstScope = provider.CreateScope();
        var document = firstScope.ServiceProvider.GetRequiredService<FrequencyMaskEditorDocument>();
        await document.InitializeAsync(new NewDocumentActivation("遮罩编辑"), default);
        document.SourcePath = "missing.png";
        document.SelectedTool = "圆环";
        document.Strength = 0.65;
        document.InvertAllCommand.Execute(null);
        var snapshot = await document.CaptureSaveSnapshotAsync(default);
        var json = snapshot.Content.Payload.GetRawText();
        Assert.Contains("invertAll", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Complex", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rgba", json, StringComparison.OrdinalIgnoreCase);

        using var secondScope = provider.CreateScope();
        var restored = secondScope.ServiceProvider.GetRequiredService<FrequencyMaskEditorDocument>();
        await restored.InitializeAsync(new RestoreDocumentActivation("恢复遮罩", snapshot.Content), default);
        Assert.Equal("圆环", restored.SelectedTool);
        Assert.Equal(0.65, restored.Strength);
        Assert.True(restored.CanUndo);
        Assert.False(restored.HasSession);
        Assert.False(restored.HasResult);
        Assert.False(restored.IsDirty);
    }

    [Fact]
    public async Task Svd快照只保存轻量参数且恢复不自动读取或分解()
    {
        var registration = new RecordingRegistration();
        registration.Services.AddSingleton<IPluginWindowInteraction, NullWindowInteraction>();
        registration.Services.AddScoped<IDocumentLifetime, TestLifetime>();
        new ImageLabPluginModule().Configure(registration);
        using var provider = registration.Services.BuildServiceProvider(validateScopes: true);
        using var firstScope = provider.CreateScope();
        var document = firstScope.ServiceProvider.GetRequiredService<SvdDecompositionDocument>();
        await document.InitializeAsync(new NewDocumentActivation("奇异值分解重建"), default);
        document.SourcePath = "missing.png";
        document.AnalysisMaximumEdge = 256;
        document.SelectedStrategy = "YCbCr 独立";
        document.SelectedChannel = "Cr";
        document.RankMaximum = 10;
        document.ComponentMaximum = 9;
        document.Rank = 7;
        document.ComponentIndex = 3;
        var snapshot = await document.CaptureSaveSnapshotAsync(default);
        var json = snapshot.Content.Payload.GetRawText();
        Assert.Contains("one-sided-jacobi-v1", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"SingularValues\":", json, StringComparison.Ordinal);
        Assert.DoesNotContain("rgba", json, StringComparison.OrdinalIgnoreCase);

        using var secondScope = provider.CreateScope();
        var restored = secondScope.ServiceProvider.GetRequiredService<SvdDecompositionDocument>();
        await restored.InitializeAsync(new RestoreDocumentActivation("恢复 SVD", snapshot.Content), default);
        Assert.Equal(256, restored.AnalysisMaximumEdge);
        Assert.Equal("YCbCr 独立", restored.SelectedStrategy);
        Assert.Equal("Cr", restored.SelectedChannel);
        Assert.Equal(7, restored.Rank);
        Assert.Equal(3, restored.ComponentIndex);
        Assert.False(restored.HasSession);
        Assert.False(restored.HasDecomposition);
        Assert.False(restored.IsDirty);
    }

    [Fact]
    public async Task 颜色迁移快照只保存轻量意图且恢复不自动读取分析或冻结调色板()
    {
        var registration = new RecordingRegistration();
        registration.Services.AddSingleton<IPluginWindowInteraction, NullWindowInteraction>();
        registration.Services.AddScoped<IDocumentLifetime, TestLifetime>();
        new ImageLabPluginModule().Configure(registration);
        using var provider = registration.Services.BuildServiceProvider(validateScopes: true);
        using var firstScope = provider.CreateScope();
        var document = firstScope.ServiceProvider.GetRequiredService<PaletteColorTransferDocument>();
        await document.InitializeAsync(new NewDocumentActivation("调色板与颜色迁移"), default);
        document.TargetPath = "target.png"; document.ReferencePath = "reference.png";
        document.ColorCount = 9; document.SelectedPaletteSource = "参考图";
        document.SelectedPaletteSort = "HSV 色相"; document.SelectedTransferMode = "保留目标 L*"; document.Strength = 0.45;
        var snapshot = await document.CaptureSaveSnapshotAsync(default);
        var json = snapshot.Content.Payload.GetRawText();
        Assert.Contains("srgb-d65-cielab-v1", json, StringComparison.Ordinal);
        Assert.DoesNotContain("rgba", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("paletteEntries", json, StringComparison.OrdinalIgnoreCase);

        using var secondScope = provider.CreateScope();
        var restored = secondScope.ServiceProvider.GetRequiredService<PaletteColorTransferDocument>();
        await restored.InitializeAsync(new RestoreDocumentActivation("恢复颜色实验", snapshot.Content), default);
        Assert.Equal(9, restored.ColorCount); Assert.Equal("参考图", restored.SelectedPaletteSource);
        Assert.Equal("HSV 色相", restored.SelectedPaletteSort); Assert.Equal("保留目标 L*", restored.SelectedTransferMode);
        Assert.Equal(0.45, restored.Strength); Assert.False(restored.HasTarget); Assert.False(restored.HasFrozenPalette);
        Assert.False(restored.HasCurrentResult); Assert.False(restored.IsDirty);
    }

    [Fact]
    public async Task 内容感知缩放快照只恢复参数且不自动读取栅格化或执行()
    {
        var registration = new RecordingRegistration();
        registration.Services.AddSingleton<IPluginWindowInteraction, NullWindowInteraction>();
        registration.Services.AddScoped<IDocumentLifetime, TestLifetime>();
        new ImageLabPluginModule().Configure(registration);
        using var provider = registration.Services.BuildServiceProvider(validateScopes: true);
        using var firstScope = provider.CreateScope();
        var document = firstScope.ServiceProvider.GetRequiredService<SeamCarvingDocument>();
        await document.InitializeAsync(new NewDocumentActivation("内容感知缩放"), default);
        document.SourcePath = "missing-private-path.png"; document.TargetWidth = 320; document.TargetHeight = 240;
        document.SelectedAxisOrder = "高优先"; document.SelectedReferenceAlgorithm = "Catmull–Rom 双三次";
        document.SelectedBrush = "优先删除"; document.BrushRadius = 21; document.PlaybackDelayMilliseconds = 250;
        var snapshot = await document.CaptureSaveSnapshotAsync(default);
        var json = snapshot.Content.Payload.GetRawText();
        Assert.Contains(SeamCarvingProtocols.SnapshotSchema, json, StringComparison.Ordinal);
        Assert.DoesNotContain("rgba", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("baseEnergy", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"EffectiveEnergy\":[", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("maskRaster", json, StringComparison.OrdinalIgnoreCase);

        using var secondScope = provider.CreateScope();
        var restored = secondScope.ServiceProvider.GetRequiredService<SeamCarvingDocument>();
        await restored.InitializeAsync(new RestoreDocumentActivation("恢复内容感知缩放", snapshot.Content), default);
        Assert.Equal(320, restored.TargetWidth); Assert.Equal(240, restored.TargetHeight);
        Assert.Equal("高优先", restored.SelectedAxisOrder); Assert.Equal("Catmull–Rom 双三次", restored.SelectedReferenceAlgorithm);
        Assert.Equal("优先删除", restored.SelectedBrush); Assert.Equal(21, restored.BrushRadius);
        Assert.Equal(250, restored.PlaybackDelayMilliseconds); Assert.False(restored.HasSession);
        Assert.False(restored.HasPlan); Assert.False(restored.HasCompletedResult); Assert.False(restored.IsDirty);
    }

    [Fact]
    public async Task Poisson快照不保存绝对路径大数组且恢复不自动读取构建或求解()
    {
        var registration = new RecordingRegistration();
        registration.Services.AddSingleton<IPluginWindowInteraction, NullWindowInteraction>();
        registration.Services.AddScoped<IDocumentLifetime, TestLifetime>();
        new ImageLabPluginModule().Configure(registration);
        using var provider = registration.Services.BuildServiceProvider(validateScopes: true);
        using var firstScope = provider.CreateScope();
        var document = firstScope.ServiceProvider.GetRequiredService<PoissonBlendingDocument>();
        await document.InitializeAsync(new NewDocumentActivation("梯度域融合"), default);
        document.SourcePath = @"C:\private\source.png"; document.TargetPath = @"D:\secret\target.png";
        document.RectangleLeft = 2; document.RectangleTop = 3; document.RectangleWidth = 10; document.RectangleHeight = 8;
        document.OffsetX = -4; document.OffsetY = 7; document.SelectedMode = "混合梯度"; document.MaxIterations = 321;
        var snapshot = await document.CaptureSaveSnapshotAsync(default); var json = snapshot.Content.Payload.GetRawText();
        Assert.Contains("source.png", json, StringComparison.Ordinal); Assert.Contains(PoissonProtocols.SnapshotSchema, json, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\private", json, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain(@"D:\secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rgba", json, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("rhs", json, StringComparison.OrdinalIgnoreCase);
        using var secondScope = provider.CreateScope(); var restored = secondScope.ServiceProvider.GetRequiredService<PoissonBlendingDocument>();
        await restored.InitializeAsync(new RestoreDocumentActivation("恢复梯度域融合", snapshot.Content), default);
        Assert.Empty(restored.SourcePath); Assert.Empty(restored.TargetPath); Assert.Equal("混合梯度", restored.SelectedMode);
        Assert.Equal(-4, restored.OffsetX); Assert.Equal(7, restored.OffsetY); Assert.Equal(321, restored.MaxIterations);
        Assert.Null(restored.Topology); Assert.False(restored.IsDirty);
    }

    [Fact]
    public async Task SpectralArt快照脱敏且恢复不自动读取栅格化或FFT()
    {
        var registration = new RecordingRegistration();
        registration.Services.AddSingleton<IPluginWindowInteraction, NullWindowInteraction>();
        registration.Services.AddScoped<IDocumentLifetime, TestLifetime>();
        new ImageLabPluginModule().Configure(registration);
        using var provider = registration.Services.BuildServiceProvider(validateScopes: true);
        using var firstScope = provider.CreateScope();
        var document = firstScope.ServiceProvider.GetRequiredService<SpectralArtDocument>();
        await document.InitializeAsync(new NewDocumentActivation("频谱艺术"), default);
        document.SourcePath = @"C:\private\carrier.png"; document.PatternImagePath = @"D:\secret\logo.png";
        document.PatternText = "private original text"; document.SelectedSampling = "灰度面积";
        document.SelectedFit = "Stretch"; document.Strength = 3.25; document.RegionLeft = 0.15; document.RegionRight = 0.35;
        var snapshot = await document.CaptureSaveSnapshotAsync(default); var json = snapshot.Content.Payload.GetRawText();
        Assert.Contains("carrier.png", json, StringComparison.Ordinal); Assert.Contains("logo.png", json, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\private", json, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain(@"D:\secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private original text", json, StringComparison.Ordinal); Assert.DoesNotContain("rgba", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("complex", json, StringComparison.OrdinalIgnoreCase);
        using var secondScope = provider.CreateScope(); var restored = secondScope.ServiceProvider.GetRequiredService<SpectralArtDocument>();
        await restored.InitializeAsync(new RestoreDocumentActivation("恢复频谱艺术", snapshot.Content), default);
        Assert.Empty(restored.SourcePath); Assert.Empty(restored.PatternImagePath); Assert.Equal("SPECTRAL", restored.PatternText);
        Assert.Equal("灰度面积", restored.SelectedSampling); Assert.Equal("Stretch", restored.SelectedFit); Assert.Equal(3.25, restored.Strength);
        Assert.False(restored.HasCarrier); Assert.False(restored.HasPattern); Assert.False(restored.HasResult); Assert.False(restored.IsDirty);
    }

    [Fact]
    public async Task 原子写入可替换目标且不遗留临时文件()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"image-lab-atomic-{Guid.NewGuid():N}");
        var target = Path.Combine(directory, "result.bin");
        Directory.CreateDirectory(directory);
        try
        {
            var writer = new AtomicFileWriter();
            await writer.WriteAsync(target, new byte[] { 1, 2, 3 }, CancellationToken.None);
            await writer.WriteAsync(target, new byte[] { 7, 8 }, CancellationToken.None);

            Assert.Equal(new byte[] { 7, 8 }, await File.ReadAllBytesAsync(target));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class RecordingRegistration : IPluginRegistration
    {
        public PluginId PluginId => PluginIds.Plugin;
        public IServiceCollection Services { get; } = new ServiceCollection();
        public List<DocumentTypeId> DocumentIds { get; } = [];
        public List<DocumentTypeId> PersistableDocumentIds { get; } = [];
        public List<ToolTypeId> ToolIds { get; } = [];

        public void UseLifecycle<TLifecycle>() where TLifecycle : class, IPluginLifecycle =>
            Services.AddSingleton<TLifecycle>();

        public void AddDocument<TDocument, TView>(DocumentDescriptor descriptor)
            where TDocument : class, IPluginDocument
            where TView : Control, new()
        {
            DocumentIds.Add(descriptor.DocumentTypeId);
            Services.AddScoped<TDocument>();
        }

        public void AddPersistableDocument<TDocument, TView>(DocumentDescriptor descriptor)
            where TDocument : class, IPersistablePluginDocument
            where TView : Control, new()
        {
            PersistableDocumentIds.Add(descriptor.DocumentTypeId);
            Services.AddScoped<TDocument>();
        }

        public void AddTool<TTool, TView>(ToolDescriptor descriptor)
            where TTool : class
            where TView : Control, new()
        {
            ToolIds.Add(descriptor.ToolTypeId);
        }
    }

    private sealed class NullWindowInteraction : IPluginWindowInteraction
    {
        public Task<IReadOnlyList<string>> PickOpenFilesAsync(FilePickerOpenOptions options, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<string?> PickSaveFileAsync(FilePickerSaveOptions options, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);

        public Task<bool> TrySetClipboardTextAsync(string text, CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }

    private sealed class TestLifetime : IDocumentLifetime
    {
        public CancellationToken ClosingToken => CancellationToken.None;
        public bool IsClosing => false;
    }
}
