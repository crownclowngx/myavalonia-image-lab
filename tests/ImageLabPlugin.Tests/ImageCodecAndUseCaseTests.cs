using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Skia;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Application.Watermarking;
using ImageLabPlugin.Application.SpectrumAnalysis;
using ImageLabPlugin.Domain.Shared.Spectral;
using ImageLabPlugin.Domain.Shared.Analysis;
using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Domain.Watermarking;
using ImageLabPlugin.Infrastructure.Cryptography;
using ImageLabPlugin.Infrastructure.ErrorCorrection;
using ImageLabPlugin.Infrastructure.Imaging;
using ImageLabPlugin.Infrastructure.Watermarking;
using ImageLabPlugin.Infrastructure.Persistence;
using MyAvaloniaManagement.PluginSdk;
using ImageLabPlugin.Features.WatermarkEmbed;
using ImageLabPlugin.Features.WatermarkInspect;
using ImageLabPlugin.Features.SpectrumInspector;
using ImageLabPlugin.Features.ImageCompareLab;
using ImageLabPlugin.Features.RobustnessLab;
using ImageLabPlugin.Domain.Robustness;
using ImageLabPlugin.Infrastructure.Robustness;
using ImageLabPlugin.Infrastructure.Perturbations;
using ImageLabPlugin.Features.ImageFingerprint;
using ImageLabPlugin.Features.BitPlaneViewer;
using ImageLabPlugin.Application.BitPlanes;
using ImageLabPlugin.Domain.BitPlanes;
using ImageLabPlugin.Application.LsbSteganography;
using ImageLabPlugin.Domain.Steganography;
using ImageLabPlugin.Domain.Shared.Perturbations;
using ImageLabPlugin.Features.LsbSteganographyLab;
using ImageLabPlugin.Features.ConvolutionPlayground;
using ImageLabPlugin.Features.WaveletLab;
using ImageLabPlugin.Features.FrequencyFilter;
using ImageLabPlugin.Features.FrequencyMaskEditor;
using ImageLabPlugin.Application.FrequencyMaskEditing;
using ImageLabPlugin.Domain.FrequencyFiltering;
using ImageLabPlugin.Domain.FrequencyMaskEditing;
using ImageLabPlugin.Features.PeriodicNoiseRemoval;
using ImageLabPlugin.Application.PeriodicNoiseRemoval;
using ImageLabPlugin.Domain.PeriodicNoiseRemoval;
using ImageLabPlugin.Features.SvdDecomposition;
using ImageLabPlugin.Features.PaletteColorTransfer;
using ImageLabPlugin.Application.ColorTransfer;
using ImageLabPlugin.Features.SeamCarving;
using ImageLabPlugin.Features.SpectralArt;
using ImageLabPlugin.Features.ImageOscilloscope;
using Xunit;

namespace ImageLabPlugin.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AvaloniaHeadlessCollection : ICollectionFixture<AvaloniaHeadlessFixture>
{
    public const string Name = "Avalonia Headless";
}

/// <summary>Avalonia 平台只能在测试进程中初始化一次，因此由集合夹具统一管理。</summary>
public sealed class AvaloniaHeadlessFixture
{
    private static readonly object Sync = new();
    private static bool _initialized;

    public AvaloniaHeadlessFixture()
    {
        lock (Sync)
        {
            if (_initialized)
            {
                return;
            }

            AppBuilder.Configure<TestApplication>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .UseSkia()
                .SetupWithoutStarting();
            _initialized = true;
        }
    }

    private sealed class TestApplication : Avalonia.Application;
}

/// <summary>验证正式 Avalonia 编解码适配器以及实际输出字节的写入—回读闭环。</summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ImageCodecAndUseCaseTests
{
    [Fact]
    public void 全部真实Document视图与图像示波器轻量控件可在Headless环境独立加载()
    {
        var embedView = new WatermarkEmbedView();
        var inspectView = new WatermarkInspectView();
        var spectrumView = new SpectrumInspectorView();
        var compareView = new ImageCompareLabView();
        var viewport = new ComparisonViewportControl();
        var histogram = new ComparisonHistogramControl();
        var robustnessView = new RobustnessLabView();
        var curve = new RobustnessCurveControl();
        var matrix = new RobustnessMatrixControl();
        var fingerprintView = new ImageFingerprintView();
        var fingerprintBitmap = new FingerprintBitmapControl();
        var fingerprintCurve = new FingerprintStabilityControl();
        var bitPlaneView = new BitPlaneViewerView();
        var bitPlanePreview = new BitPlanePreviewControl();
        var lsbView = new LsbSteganographyLabView();
        var convolutionView = new ConvolutionPlaygroundView();
        var waveletView = new WaveletLabView();
        var waveletPyramid = new WaveletPyramidControl();
        var waveletChart = new WaveletScanChartControl();
        var frequencyFilterView = new FrequencyFilterView();
        var frequencyMaskView = new FrequencyMaskEditorView();
        var frequencyMaskCanvas = new FrequencyMaskCanvasControl();
        var periodicNoiseView = new PeriodicNoiseRemovalView();
        var periodicSpectrum = new PeriodicSpectrumControl();
        var svdView = new SvdDecompositionView();
        var svdCurve = new SingularValueCurveControl();
        var paletteColorView = new PaletteColorTransferView();
        var paletteStrip = new PaletteStripControl();
        var colorHistogram = new ColorHistogramControl();
        var colorPlane = new ColorDistributionPlaneControl();
        var differenceHistogram = new PerceptualDifferenceControl();
        var seamView = new SeamCarvingView();
        var seamOverlay = new SeamOverlayCanvas();
        var energyMap = new EnergyMapControl();
        var seamComparison = new SeamComparisonControl();
        var spectralArtView = new SpectralArtView();
        var spectralRegion = new SpectralArtRegionCanvas();
        var imageOscilloscopeView = new ImageOscilloscopeView();
        var scopePlot = new ScopePlotControl();
        var scopeHistogram = new ScopeHistogramControl();

        Assert.NotNull(embedView.Content);
        Assert.NotNull(inspectView.Content);
        Assert.NotSame(embedView.Content, inspectView.Content);
        Assert.NotNull(spectrumView.Content);
        Assert.NotSame(inspectView.Content, spectrumView.Content);
        Assert.NotNull(compareView.Content);
        Assert.NotSame(spectrumView.Content, compareView.Content);
        Assert.NotNull(viewport);
        Assert.NotNull(histogram);
        Assert.NotNull(robustnessView.Content);
        Assert.NotNull(curve);
        Assert.NotNull(matrix);
        Assert.NotNull(fingerprintView.Content);
        Assert.NotNull(fingerprintBitmap);
        Assert.NotNull(fingerprintCurve);
        Assert.NotNull(bitPlaneView.Content);
        Assert.NotNull(bitPlanePreview);
        Assert.NotNull(lsbView.Content);
        Assert.NotSame(bitPlaneView.Content, lsbView.Content);
        Assert.NotNull(convolutionView.Content);
        Assert.NotNull(waveletView.Content);
        Assert.NotNull(waveletPyramid);
        Assert.NotNull(waveletChart);
        Assert.NotNull(frequencyFilterView.Content);
        Assert.NotNull(frequencyMaskView.Content);
        Assert.NotNull(frequencyMaskCanvas);
        Assert.NotNull(periodicNoiseView.Content);
        Assert.NotNull(periodicSpectrum);
        Assert.NotNull(svdView.Content);
        Assert.NotNull(svdCurve);
        Assert.NotNull(paletteColorView.Content);
        Assert.NotNull(paletteStrip); Assert.NotNull(colorHistogram); Assert.NotNull(colorPlane); Assert.NotNull(differenceHistogram);
        Assert.NotNull(seamView.Content); Assert.NotNull(seamOverlay); Assert.NotNull(energyMap); Assert.NotNull(seamComparison);
        Assert.NotNull(spectralArtView.Content); Assert.NotNull(spectralRegion);
        Assert.NotNull(imageOscilloscopeView.Content); Assert.NotNull(scopePlot); Assert.NotNull(scopeHistogram);
        Assert.NotSame(lsbView.Content, convolutionView.Content);
    }

    [Fact]
    public void 所有Document数值输入框保留可读编辑宽度()
    {
        UserControl[] views =
        [
            new WatermarkEmbedView(),
            new SpectrumInspectorView(),
            new ImageCompareLabView(),
            new RobustnessLabView(),
            new BitPlaneViewerView(),
            new LsbSteganographyLabView(),
            new ConvolutionPlaygroundView(),
            new FrequencyFilterView(),
            new FrequencyMaskEditorView(),
            new PeriodicNoiseRemovalView(),
            new SvdDecompositionView(),
            new PaletteColorTransferView(),
            new SeamCarvingView(),
            new SpectralArtView(),
            new ImageOscilloscopeView()
        ];

        var numericInputs = new List<NumericUpDown>();
        foreach (var view in views)
        {
            var window = new Window { Width = 1280, Height = 850, Content = view };
            window.Show();
            var viewInputs = view.GetLogicalDescendants().OfType<NumericUpDown>().ToArray();
            Assert.All(viewInputs, input =>
            {
                Assert.True(input.MinWidth >= 120);
                Assert.True(input.Bounds.Width >= 120);
            });
            numericInputs.AddRange(viewInputs);
            window.Close();
        }

        Assert.Equal(74, numericInputs.Count);
    }

    [Fact]
    public void 频谱遮罩手势在capture丢失回调前冻结完整路径()
    {
        var state = new FrequencyMaskGestureState();
        var start = new NormalizedFrequencyPoint(0.2d, 0.4d);
        var middle = new NormalizedFrequencyPoint(0.5d, 0.4d);
        var end = new NormalizedFrequencyPoint(0.8d, 0.4d);
        state.Begin(start);
        state.Append(middle);
        state.Append(end);

        var releaseWasCalled = false;
        var completed = state.Complete(() =>
        {
            releaseWasCalled = true;
            // 模拟 e.Pointer.Capture(null) 同步触发 OnPointerCaptureLost。
            state.Cancel();
        });

        Assert.True(releaseWasCalled);
        Assert.Equal([start, middle, end], completed);
        Assert.False(state.IsActive);
        Assert.Empty(state.Points);
    }

    [Fact]
    public async Task Lsb使用正式Png编解码完成真实双重回读且Jpeg预设返回Ber()
    {
        var codec = new AvaloniaImageCodec();
        var source = CreateTexturedImage(40, 40, includeAlpha: false);
        var session = new LsbExperimentSession("memory.png", source, new LsbSlotLayout(source));
        var recipe = new LsbRecipe(LsbChannelStrategy.RgbRoundRobin, 0, LsbPlacementKind.PseudoRandom, 20260830);
        ILsbSlotOrder[] orders = [new SequentialLsbSlotOrder(), new PseudoRandomLsbSlotOrder()];
        using var payload = LsbPayload.FromText("真实 PNG 回读");
        var embed = new EmbedAndAnalyzeLsbUseCase(new LsbFrameCodec(), new LsbCapacityCalculator(), new LsbEmbeddingEngine(orders),
            new LsbExtractionEngine(new LsbFrameCodec(), orders), new LsbStatisticsAnalyzer(), new LsbPreviewProjector(), codec);

        var result = await embed.ExecuteAsync(session, payload, recipe, LsbStatisticsScope.EligibleImage, CancellationToken.None);
        var fragility = new RunLsbFragilityUseCase([new JpegReencodeOperator(codec)], new LsbExtractionEngine(new LsbFrameCodec(), orders),
            new FullReferenceQualityAnalyzer(new ImagePairValidator()));
        var attacked = await fragility.ExecuteAsync(session, LsbFragilityPreset.Jpeg95, CancellationToken.None);

        Assert.Equal(LsbReadStatus.Success, result.SelfCheck.Status);
        Assert.True(session.HasVerifiedStego);
        Assert.Equal(source.Size, attacked.Image.Size);
        Assert.True(attacked.FrameBer.ComparedBits > 0);
        Assert.NotNull(attacked.PsnrRgbDb);
    }

    [Fact]
    public async Task 频谱遮罩Document完成真实Png载入手势重建撤销和轻量快照()
    {
        var codec = new AvaloniaImageCodec();
        var input = Path.Combine(Path.GetTempPath(), $"frequency-mask-document-{Guid.NewGuid():N}.png");
        try
        {
            await File.WriteAllBytesAsync(input, await codec.EncodeAsync(CreateTexturedImage(16, 16, includeAlpha: true),
                ImageOutputFormat.Png, 100, default));
            using var document = CreateFrequencyMaskDocument(codec);
            await document.InitializeAsync(new NewDocumentActivation("遮罩交互"), default);
            document.SourcePath = input;
            document.AnalysisMaximumEdge = 512;
            await document.PrepareCommand.ExecuteAsync(null);
            Assert.True(document.HasSession);
            Assert.True(document.HasResult);

            document.CommitGesture([new NormalizedFrequencyPoint(8d / 15d, 8d / 15d)]);
            Assert.True(document.CanUndo);
            await WaitUntilAsync(() => document.HasResult);
            Assert.Contains("编辑", document.DiagnosticsSummary, StringComparison.Ordinal);
            document.UndoCommand.Execute(null);
            Assert.True(document.CanRedo);
            await WaitUntilAsync(() => document.HasResult);

            var snapshot = await document.CaptureSaveSnapshotAsync(default);
            var json = snapshot.Content.Payload.GetRawText();
            Assert.DoesNotContain("Complex", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("rgba", json, StringComparison.OrdinalIgnoreCase);
        }
        finally { File.Delete(input); }
    }

    [Fact]
    public async Task 周期噪声Document严格执行草案采用重建与导出状态机()
    {
        var codec = new AvaloniaImageCodec();
        var input = Path.Combine(Path.GetTempPath(), $"periodic-noise-document-{Guid.NewGuid():N}.png");
        try
        {
            await File.WriteAllBytesAsync(input, await codec.EncodeAsync(CreateTexturedImage(32, 32,
                includeAlpha: true), ImageOutputFormat.Png, 100, default));
            using var document = CreatePeriodicNoiseDocument(codec);
            await document.InitializeAsync(new NewDocumentActivation("周期噪声状态机"), default);
            document.SourcePath = input;
            document.AnalysisMaximumEdge = 512;
            await document.PrepareCommand.ExecuteAsync(null);
            Assert.True(document.HasSession);

            document.ToggleSpectrumPoint(0.65, 0.5);
            Assert.True(document.HasDraft);
            await document.PreviewCommand.ExecuteAsync(null);
            Assert.False(document.CanExport);
            Assert.Contains("未确认草案", document.DiagnosticsSummary, StringComparison.Ordinal);

            document.AcceptDraftCommand.Execute(null);
            Assert.True(document.HasAcceptedRecipe);
            Assert.False(document.CanExport);
            await document.PreviewCommand.ExecuteAsync(null);
            Assert.True(document.CanExport);
            Assert.Contains("已采用", document.DiagnosticsSummary, StringComparison.Ordinal);

            var snapshot = await document.CaptureSaveSnapshotAsync(default);
            var json = snapshot.Content.Payload.GetRawText();
            Assert.DoesNotContain("rgba", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("spectrum", json, StringComparison.OrdinalIgnoreCase);

            using var restored = CreatePeriodicNoiseDocument(codec);
            await restored.InitializeAsync(new RestoreDocumentActivation("恢复周期噪声", snapshot.Content), default);
            Assert.True(restored.HasAcceptedRecipe);
            Assert.False(restored.HasSession);
            await restored.PrepareCommand.ExecuteAsync(null);
            Assert.True(restored.HasSession);
            Assert.True(restored.HasAcceptedRecipe);
            Assert.False(restored.CanExport);
        }
        finally { File.Delete(input); }
    }

    [Fact]
    public async Task Png编解码保持尺寸颜色和透明度()
    {
        var codec = new AvaloniaImageCodec();
        var source = CreateTexturedImage(64, 48, includeAlpha: true);

        var encoded = await codec.EncodeAsync(source, ImageOutputFormat.Png, 95, CancellationToken.None);
        var decoded = await codec.DecodeAsync(encoded, CancellationToken.None);

        Assert.Equal(source.Size, decoded.Size);
        var expected = source.Rgba.Span;
        var actual = decoded.Rgba.Span;
        for (var i = 0; i < expected.Length; i += 4)
        {
            // PNG 本身无损；半透明像素经过 Skia 的预乘/反预乘表示时，隐藏 RGB 可能产生极小舍入误差。
            Assert.InRange(Math.Abs(expected[i] - actual[i]), 0, 3);
            Assert.InRange(Math.Abs(expected[i + 1] - actual[i + 1]), 0, 3);
            Assert.InRange(Math.Abs(expected[i + 2] - actual[i + 2]), 0, 3);
            Assert.Equal(expected[i + 3], actual[i + 3]);
        }
    }

    [Fact]
    public async Task 颜色结果Png执行真实回读原子发布并阻止覆盖输入()
    {
        var codec = new AvaloniaImageCodec(); var writer = new AtomicFileWriter();
        var useCase = new ExportColorResultUseCase(codec, writer);
        var source = CreateTexturedImage(16, 12, includeAlpha: true);
        var directory = Path.Combine(Path.GetTempPath(), $"palette-color-export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory); var input = Path.Combine(directory, "input.png");
        var output = Path.Combine(directory, "result.png");
        try
        {
            await useCase.ExecuteAsync(source, output, input, default);
            var decoded = await codec.DecodeAsync(output, default); Assert.Equal(source.Size, decoded.Size);
            for (var i = 3; i < source.Rgba.Length; i += 4) Assert.Equal(source.Rgba.Span[i], decoded.Rgba.Span[i]);
            await Assert.ThrowsAsync<InvalidOperationException>(() => useCase.ExecuteAsync(source, input, input, default));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task 鲁棒性JpegStrategy使用正式Codec并阻断Alpha()
    {
        var operation = new JpegReencodeOperator(new AvaloniaImageCodec());
        var key = new RobustnessCaseKey(RobustnessProfileId.Robust, 0, 95m, 0);
        await Assert.ThrowsAsync<InvalidOperationException>(() => operation.ApplyAsync(
            CreateTexturedImage(64, 48, includeAlpha: true), new JpegParameters(95), PerturbationTestContext.From(1, key, "jpeg", PerturbationKind.JpegReencode), default).AsTask());

        var opaque = CreateTexturedImage(64, 48, includeAlpha: false);
        var output = await operation.ApplyAsync(opaque, new JpegParameters(95), PerturbationTestContext.From(1, key, "jpeg", PerturbationKind.JpegReencode), default);
        Assert.Equal(opaque.Size, output.Size); Assert.NotEqual(opaque.Rgba.ToArray(), output.Rgba.ToArray());
    }

    [Fact]
    public async Task 频域Document使用正式编解码器完成分析重建与预览闭环()
    {
        var codec = new AvaloniaImageCodec();
        var source = CreateTexturedImage(64, 48, includeAlpha: true);
        var bytes = await codec.EncodeAsync(source, ImageOutputFormat.Png, 100, CancellationToken.None);
        var path = Path.Combine(Path.GetTempPath(), $"image-lab-spectrum-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(path, bytes);
        var channel = new ImageChannelConverter();
        var fft = new Fft2DTransform(new Fft1DTransform());
        var spectrumProjector = new SpectrumProjector();
        var radial = new RadialEnergyAnalyzer();
        var dct = new Dct8x8Transform();
        using var document = new SpectrumInspectorDocument(
            new AnalyzeSpectrumUseCase(codec, new ImageAnalysisProxyProjector(), channel, fft, spectrumProjector,
                new DctSpectrumProjector(channel, dct), radial),
            new InspectDctBlockUseCase(new DctBlockAnalyzer(channel, dct)),
            new ReconstructSpectrumBandUseCase(fft, new FrequencyBandMaskFactory(), channel),
            new ProjectSpectrumUseCase(spectrumProjector, radial),
            new NullImageDialog(), codec, new AtomicFileWriter(), new TestDocumentLifetime());
        try
        {
            await document.InitializeAsync(new NewDocumentActivation("频域闭环"), CancellationToken.None);
            document.SourcePath = path;
            await document.AnalyzeCommand.ExecuteAsync(null);

            Assert.True(document.HasSession);
            Assert.True(document.HasReconstruction);
            Assert.NotNull(document.SourcePreview);
            Assert.NotNull(document.SpectrumPreview);
            Assert.NotNull(document.MaskPreview);
            Assert.NotNull(document.ReconstructionPreview);
            Assert.Equal(256, document.RadialBins.Count);

            document.SelectedBand = "高频";
            await document.ReconstructCommand.ExecuteAsync(null);
            Assert.Contains("重建完成", document.StatusMessage, StringComparison.Ordinal);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task 位平面Document使用正式编解码器完成五通道统计与四预览闭环()
    {
        var codec = new AvaloniaImageCodec();
        var source = CreateTexturedImage(64, 48, includeAlpha: true);
        var bytes = await codec.EncodeAsync(source, ImageOutputFormat.Png, 100, CancellationToken.None);
        var path = Path.Combine(Path.GetTempPath(), $"image-lab-bit-plane-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(path, bytes);
        var extractor = new BitPlaneChannelExtractor();
        var statistics = new BitPlaneStatisticsAnalyzer();
        using var document = new BitPlaneViewerDocument(
            new PrepareBitPlaneSessionUseCase(codec),
            new AnalyzeBitPlaneChannelUseCase(extractor, statistics),
            new ProjectBitPlaneViewUseCase(new BitPlaneProjector(), new BitPlanePixelInspector()),
            new ExportBitPlaneImageUseCase(new BitPlaneReconstructor(), codec, new AtomicFileWriter()),
            new NullImageDialog(), codec, new TestDocumentLifetime());
        try
        {
            await document.InitializeAsync(new NewDocumentActivation("位平面闭环"), CancellationToken.None);
            document.SourcePath = path;
            await document.AnalyzeCommand.ExecuteAsync(null);

            Assert.True(document.HasSession);
            Assert.NotNull(document.SourcePreview);
            Assert.NotNull(document.FocusedPreview);
            Assert.NotNull(document.CombinedPreview);
            Assert.NotNull(document.ReconstructionPreview);
            Assert.Equal(8, document.BitRows.Count);
            Assert.All(document.BitRows, row => Assert.NotNull(row.Statistics));
            Assert.Contains("投影完成", document.StatusMessage, StringComparison.Ordinal);

            document.SelectedChannel = "Alpha";
            var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (document.IsBusy && DateTime.UtcNow < timeout) await Task.Delay(10);
            Assert.True(document.IsAlphaChannel);
            Assert.True(document.ShowReconstructionCheckerboard);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task 位平面Document拒绝忽略取消的旧图片迟到结果()
    {
        var codec = new AvaloniaImageCodec();
        var prepare = new SequencedBitPlanePrepareUseCase();
        var firstPath = Path.GetTempFileName();
        var secondPath = Path.GetTempFileName();
        using var document = new BitPlaneViewerDocument(
            prepare,
            new AnalyzeBitPlaneChannelUseCase(new BitPlaneChannelExtractor(), new BitPlaneStatisticsAnalyzer()),
            new ProjectBitPlaneViewUseCase(new BitPlaneProjector(), new BitPlanePixelInspector()),
            new ExportBitPlaneImageUseCase(new BitPlaneReconstructor(), codec, new AtomicFileWriter()),
            new NullImageDialog(), codec, new TestDocumentLifetime());
        try
        {
            await document.InitializeAsync(new NewDocumentActivation("迟到门禁"), CancellationToken.None);
            document.SourcePath = firstPath;
            var first = document.AnalyzeCommand.ExecuteAsync(null);
            await prepare.WaitForCallsAsync(1);
            document.SourcePath = secondPath;
            var second = document.AnalyzeCommand.ExecuteAsync(null);
            await prepare.WaitForCallsAsync(2);

            prepare.Complete(1, new PixelImage(new ImageSize(2, 1), [9, 8, 7, 6, 5, 4, 3, 2]));
            await second;
            prepare.Complete(0, new PixelImage(new ImageSize(1, 1), [1, 2, 3, 4]));
            await first;

            Assert.Contains("2×1", document.ImageSummary, StringComparison.Ordinal);
            Assert.DoesNotContain("1×1", document.ImageSummary, StringComparison.Ordinal);
            Assert.True(document.HasSession);
        }
        finally { File.Delete(firstPath); File.Delete(secondPath); }
    }

    [Fact]
    public async Task Jpeg编解码保持尺寸并产生可用像素()
    {
        var codec = new AvaloniaImageCodec();
        var source = CreateTexturedImage(64, 48, includeAlpha: false);

        var encoded = await codec.EncodeAsync(source, ImageOutputFormat.Jpeg, 95, CancellationToken.None);
        var decoded = await codec.DecodeAsync(encoded, CancellationToken.None);
        var quality = new FullReferenceQualityAnalyzer(new ImagePairValidator()).Analyze(source, decoded);

        Assert.Equal(source.Size, decoded.Size);
        Assert.True(encoded.Length > 100);
        Assert.True(quality.PsnrLumaDb > 20d);
        Assert.True(quality.GlobalSsimLuma > 0.70d);
    }

    [Fact]
    public async Task 超大编码文件在整体读取和图片解码前被拒绝()
    {
        var path = Path.Combine(Path.GetTempPath(), $"image-lab-oversized-{Guid.NewGuid():N}.png");
        try
        {
            await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.SetLength((long)AvaloniaImageCodec.MaximumEncodedBytes + 1);
            }

            var codec = new AvaloniaImageCodec();
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                codec.DecodeAsync(path, CancellationToken.None));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Png正式输出按实际文件字节完成自检和提取()
    {
        var pipeline = CreatePipeline();
        var source = CreateTexturedImage(512, 512, includeAlpha: false);
        var sourceBytes = await pipeline.Codec.EncodeAsync(source, ImageOutputFormat.Png, 95, CancellationToken.None);
        var sourcePath = Path.Combine(Path.GetTempPath(), $"image-lab-source-{Guid.NewGuid():N}.png");
        try
        {
            await File.WriteAllBytesAsync(sourcePath, sourceBytes);
            var payload = new WatermarkPayload(Encoding.UTF8.GetBytes("正式 PNG 输出闭环"), PayloadContentType.Text);

            var result = await pipeline.Embed.ExecuteAsync(
                new EmbedWatermarkRequest(
                    sourcePath,
                    payload,
                    EmbeddingProfileId.Balanced,
                    Password: null,
                    ImageOutputFormat.Png),
                CancellationToken.None);
            var report = await pipeline.Extract.ExecuteAsync(result.EncodedImage, null, CancellationToken.None);

            Assert.Equal("PNG", result.OutputFormat);
            Assert.True(result.Capacity.Fits);
            Assert.Equal(WatermarkDetectionStatus.RecoveredIntegrityValid, report.Status);
            Assert.Equal(payload.Bytes.ToArray(), report.Payload.ToArray());
            Assert.True(result.Quality.Psnr > 20d);
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Fact]
    public async Task Jpeg高质量鲁棒配置按实际输出字节完成自检()
    {
        var pipeline = CreatePipeline();
        var source = CreateTexturedImage(768, 768, includeAlpha: false);
        var sourceBytes = await pipeline.Codec.EncodeAsync(source, ImageOutputFormat.Png, 95, CancellationToken.None);
        var sourcePath = Path.Combine(Path.GetTempPath(), $"image-lab-jpeg-source-{Guid.NewGuid():N}.png");
        try
        {
            await File.WriteAllBytesAsync(sourcePath, sourceBytes);
            var payload = new WatermarkPayload(Encoding.UTF8.GetBytes("JPEG"), PayloadContentType.Text);

            var result = await pipeline.Embed.ExecuteAsync(
                new EmbedWatermarkRequest(
                    sourcePath,
                    payload,
                    EmbeddingProfileId.Robust,
                    Password: null,
                    ImageOutputFormat.Jpeg,
                    JpegQuality: 100),
                CancellationToken.None);

            Assert.Equal("JPEG", result.OutputFormat);
            Assert.Equal(WatermarkDetectionStatus.RecoveredIntegrityValid, result.SelfCheck.Status);
            Assert.Equal(payload.Bytes.ToArray(), result.SelfCheck.Payload.ToArray());
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Fact]
    public async Task 鲁棒配置可通过一次Jpeg质量九十五重编码()
    {
        var pipeline = CreatePipeline();
        var source = CreateTexturedImage(768, 768, includeAlpha: false);
        using var payload = new WatermarkPayload(Encoding.UTF8.GetBytes("jpeg-95-attack"), PayloadContentType.Text);
        var errorCorrection = new ReedSolomonCodec();
        var protocol = new WatermarkFrameProtocol(errorCorrection, new CryptographicRandomSource());
        var carrier = new FrequencyWatermarkCarrier(new Dct8x8Transform(), protocol, errorCorrection);
        var frame = protocol.Encode(payload, EmbeddingProfileId.Robust, password: null);
        var embedded = carrier.Embed(source, frame, CancellationToken.None);

        var jpeg = await pipeline.Codec.EncodeAsync(embedded, ImageOutputFormat.Jpeg, 95, CancellationToken.None);
        var report = await pipeline.Extract.ExecuteAsync(jpeg, password: null, CancellationToken.None);

        Assert.Contains(
            report.Status,
            new[] { WatermarkDetectionStatus.RecoveredIntegrityValid, WatermarkDetectionStatus.RecoveredWithCorrections });
        Assert.Equal(payload.Bytes.ToArray(), report.Payload.ToArray());
    }

    [Fact]
    public async Task 随机普通图片不会被误判为支持的水印()
    {
        var pipeline = CreatePipeline();
        var report = pipeline.Extract.Extract(
            CreateTexturedImage(512, 512, includeAlpha: false),
            password: null,
            CancellationToken.None);

        Assert.Equal(WatermarkDetectionStatus.NoSupportedWatermark, report.Status);
        Assert.Empty(report.Payload.ToArray());
        await Task.CompletedTask;
    }

    private static (AvaloniaImageCodec Codec, EmbedWatermarkUseCase Embed, ExtractWatermarkUseCase Extract) CreatePipeline()
    {
        var codec = new AvaloniaImageCodec();
        var errorCorrection = new ReedSolomonCodec();
        var protocol = new WatermarkFrameProtocol(errorCorrection, new CryptographicRandomSource());
        var carrier = new FrequencyWatermarkCarrier(new Dct8x8Transform(), protocol, errorCorrection);
        var extract = new ExtractWatermarkUseCase(codec, carrier, protocol);
        return (
            codec,
            new EmbedWatermarkUseCase(
                codec,
                protocol,
                carrier,
                new FrequencySpectrumProjector(new Dct8x8Transform()),
                new FullReferenceQualityAnalyzer(new ImagePairValidator()),
                extract),
            extract);
    }

    private static PixelImage CreateTexturedImage(int width, int height, bool includeAlpha)
    {
        var rgba = new byte[checked(width * height * 4)];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = ((y * width) + x) * 4;
                var checker = (((x / 8) + (y / 8)) & 1) == 0 ? 17 : -17;
                rgba[offset] = (byte)Math.Clamp(70 + ((x * 7 + y * 3) % 150) + checker, 0, 255);
                rgba[offset + 1] = (byte)Math.Clamp(60 + ((x * 5 + y * 11) % 155) - checker, 0, 255);
                rgba[offset + 2] = (byte)Math.Clamp(55 + ((x * 13 + y * 2) % 160), 0, 255);
                rgba[offset + 3] = includeAlpha ? (byte)(80 + ((x * 9 + y * 7) % 176)) : (byte)255;
            }
        }

        return new PixelImage(new ImageSize(width, height), rgba);
    }

    private static FrequencyMaskEditorDocument CreateFrequencyMaskDocument(IImageCodec codec)
    {
        var fft = new Fft2DTransform(new Fft1DTransform());
        var converter = new ImageChannelConverter();
        var builder = new FrequencySpectrumBuilder(fft);
        var rasterizer = new FrequencyMaskRasterizer(new ConjugateMaskWriter());
        var render = new RenderFrequencyMaskUseCase(rasterizer, new FrequencyMaskApplier(fft), converter,
            new FrequencyMaskDiagnostics(), new ChannelDifferenceProjector(),
            new FullReferenceQualityAnalyzer(new ImagePairValidator()));
        var serializer = new FrequencyMaskRecipeSerializer();
        return new FrequencyMaskEditorDocument(
            new PrepareFrequencyMaskEditorSessionUseCase(codec, new ImageAnalysisProxyProjector(), converter, builder, new SpectrumProjector()),
            render, new RenderFullFrequencyMaskUseCase(converter, builder, render),
            new ExportFrequencyMaskImageUseCase(codec, new AtomicFileWriter()),
            new ImportFrequencyMaskRecipeUseCase(new BoundedTextFileReader(), serializer),
            new ExportFrequencyMaskRecipeUseCase(serializer, new AtomicFileWriter()), serializer,
            new InspectFrequencyMaskPointUseCase(), new NullImageDialog(), new NullRecipeDialog(), codec,
            new TestDocumentLifetime());
    }

    private static PeriodicNoiseRemovalDocument CreatePeriodicNoiseDocument(IImageCodec codec)
    {
        var fft = new Fft2DTransform(new Fft1DTransform());
        var converter = new ImageChannelConverter();
        var builder = new FrequencySpectrumBuilder(fft);
        var render = new RenderPeriodicNoisePreviewUseCase(new NotchMaskFactory(new NotchResponse()),
            new FrequencyMaskApplier(fft), new FrequencyGainSpectrumProjector(new SpectrumProjector()), converter,
            new ChannelDifferenceProjector(), new FullReferenceQualityAnalyzer(new ImagePairValidator()),
            new PeriodicNoiseLossAnalyzer());
        var serializer = new PeriodicNoiseRecipeSerializer();
        return new PeriodicNoiseRemovalDocument(
            new PreparePeriodicNoiseSessionUseCase(codec, new ImageAnalysisProxyProjector(), converter, builder,
                new SpectrumProjector()),
            new DetectPeriodicNoiseCandidatesUseCase(new PeriodicPeakDetector(new RadialLogPowerBaseline(),
                new PeriodicPeakRiskAssessor())), new MapPeriodicSpectrumSelectionUseCase(), render,
            new RenderFullPeriodicNoiseResultUseCase(converter, builder, render),
            new ImportPeriodicNoiseRecipeUseCase(new BoundedTextFileReader(), serializer),
            new ExportPeriodicNoiseRecipeUseCase(serializer, new AtomicFileWriter()),
            new ExportPeriodicNoiseCandidateSummaryUseCase(new PeriodicNoiseCandidateSummarySerializer(),
                new AtomicFileWriter()), new ExportPeriodicNoiseArtifactUseCase(codec, new AtomicFileWriter()),
            new NullImageDialog(), new NullPeriodicNoiseDialog(), codec, new TestDocumentLifetime());
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < timeout)
        {
            if (predicate()) return;
            await Task.Delay(20);
        }
        throw new TimeoutException("等待频谱遮罩 Document 状态超时。");
    }

    private sealed class NullImageDialog : IImageFileDialog
    {
        public Task<string?> PickImageAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task<string?> PickOutputImageAsync(string suggestedName, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }

    private sealed class NullRecipeDialog : IFrequencyMaskRecipeFileDialog
    {
        public Task<string?> PickRecipeInputAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task<string?> PickRecipeOutputAsync(string suggestedName, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }

    private sealed class NullPeriodicNoiseDialog : IPeriodicNoiseFileDialog
    {
        public Task<string?> PickRecipeInputAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task<string?> PickRecipeOutputAsync(string suggestedName, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);
        public Task<string?> PickCandidateSummaryOutputAsync(string suggestedName, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);
    }

    private sealed class SequencedBitPlanePrepareUseCase : IPrepareBitPlaneSessionUseCase
    {
        private readonly List<(string Path, TaskCompletionSource<BitPlaneSession> Completion)> _calls = [];

        public Task<BitPlaneSession> ExecuteAsync(string sourcePath, CancellationToken cancellationToken)
        {
            // 故意忽略取消，专门证明 Document 的 generation 门禁独立于底层合作程度。
            var completion = new TaskCompletionSource<BitPlaneSession>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_calls) _calls.Add((sourcePath, completion));
            return completion.Task;
        }

        public async Task WaitForCallsAsync(int count)
        {
            var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < timeout)
            {
                lock (_calls) if (_calls.Count >= count) return;
                await Task.Delay(10);
            }
            throw new TimeoutException("未观察到预期的位平面准备调用。");
        }

        public void Complete(int index, PixelImage image)
        {
            (string Path, TaskCompletionSource<BitPlaneSession> Completion) call;
            lock (_calls) call = _calls[index];
            call.Completion.SetResult(new BitPlaneSession(call.Path, image));
        }
    }

    private sealed class TestDocumentLifetime : IDocumentLifetime
    {
        public CancellationToken ClosingToken => CancellationToken.None;
        public bool IsClosing => false;
    }
}
