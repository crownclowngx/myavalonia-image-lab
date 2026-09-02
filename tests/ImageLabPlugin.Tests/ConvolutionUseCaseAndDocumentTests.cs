using ImageLabPlugin.Application.Convolution;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.Convolution;
using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Domain.Shared.Spatial;
using ImageLabPlugin.Features.ConvolutionPlayground;
using MyAvaloniaManagement.PluginSdk;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class ConvolutionUseCaseAndDocumentTests
{
    [Fact]
    public async Task 准备会话只解码一次并建立受控代理()
    {
        var codec = new RecordingCodec(CreateImage());
        using var session = await new PrepareConvolutionSessionUseCase(codec, new ImageAnalysisProxyProjector())
            .ExecuteAsync("source.png", 512, CancellationToken.None);
        Assert.Equal(1, codec.DecodeCalls); Assert.Equal(new ImageSize(2, 1), session.SourceImage.Size); Assert.NotSame(session.SourceImage, session.AnalysisProxy);
    }

    [Fact]
    public async Task 预览与完整尺寸分别执行并绑定同一配方指纹()
    {
        var recipe = CreateRecipe(); using var session = new ConvolutionSession("source.png", CreateImage(), CreateImage(), 512);
        var processor = CreateProcessor();
        var preview = await new RenderConvolutionPreviewUseCase(processor, new ConvolutionDifferenceProjector()).ExecuteAsync(session, recipe, CancellationToken.None);
        var full = await new RenderFullConvolutionUseCase(processor).ExecuteAsync(session, recipe, CancellationToken.None);
        Assert.Equal(recipe.Fingerprint(), preview.RecipeFingerprint); Assert.Equal(recipe.Fingerprint(), full.RecipeFingerprint);
        Assert.Equal(new byte[] { 10, 20, 30, 4, 40, 50, 60, 8 }, full.Image.Rgba.ToArray());
    }

    [Fact]
    public async Task 导出固定Png且拒绝过期指纹()
    {
        var codec = new RecordingCodec(CreateImage()); var writer = new RecordingWriter(); var recipe = CreateRecipe();
        var full = new FullConvolutionResult(CreateImage(), [], recipe.Fingerprint(), TimeSpan.Zero, 0);
        var useCase = new ExportConvolutionImageUseCase(codec, writer);
        await Assert.ThrowsAsync<InvalidOperationException>(() => useCase.ExecuteAsync(full, "stale", "output.png", CancellationToken.None));
        var exported = await useCase.ExecuteAsync(full, recipe.Fingerprint(), "output.png", CancellationToken.None);
        Assert.Equal(ImageOutputFormat.Png, codec.LastFormat); Assert.Equal("output.png", writer.Path); Assert.Equal("output.png", exported.OutputPath);
    }

    [Fact]
    public async Task 已释放会话拒绝后续计算()
    {
        var session = new ConvolutionSession("source.png", CreateImage(), CreateImage(), 512); session.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => new RenderFullConvolutionUseCase(CreateProcessor()).ExecuteAsync(session, CreateRecipe(), CancellationToken.None));
    }

    [Fact]
    public async Task Document快照只含轻量参数与有限系数且恢复不自动解码()
    {
        var codec = new RecordingCodec(CreateImage()); using var source = CreateDocument(codec);
        await source.InitializeAsync(new NewDocumentActivation("卷积"), CancellationToken.None);
        source.SourcePath = "D:/missing/source.png"; source.KernelText = "1 0 0\n0 1 0\n0 0 1";
        source.SelectedBorder = "Wrap"; source.Bias = 12; source.AnalysisMaximumEdge = 512;
        var snapshot = await source.CaptureSaveSnapshotAsync(CancellationToken.None); var json = snapshot.Content.Payload.GetRawText();
        Assert.Contains("CustomCoefficients", json, StringComparison.Ordinal); Assert.DoesNotContain("Rgba", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bitmap", json, StringComparison.OrdinalIgnoreCase); Assert.Equal(0, codec.DecodeCalls);

        using var restored = CreateDocument(codec);
        await restored.InitializeAsync(new RestoreDocumentActivation("恢复卷积", snapshot.Content), CancellationToken.None);
        Assert.Equal("Wrap", restored.SelectedBorder); Assert.Equal(12, restored.Bias); Assert.Equal(512, restored.AnalysisMaximumEdge);
        Assert.Equal(0, codec.DecodeCalls); Assert.Contains("不存在", restored.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 未知Schema安全保留默认核且不抛出()
    {
        using var document = CreateDocument(new RecordingCodec(CreateImage()));
        await document.InitializeAsync(new RestoreDocumentActivation("未知", new DocumentContent(99,
            System.Text.Json.JsonSerializer.SerializeToElement(new { }))), CancellationToken.None);
        Assert.Equal("gaussian", document.SelectedPreset); Assert.Contains("不支持 schema", document.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void 生产代码没有引入Aiflow工作流命令或通用Dag()
    {
        var root = FindRepositoryRoot(); var sourceRoot = Path.Combine(root, "src");
        var production = string.Join('\n', Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(sourceRoot, "*.axaml", SearchOption.AllDirectories))
            .Select(File.ReadAllText));
        Assert.DoesNotContain("AIFLOW", production, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WorkflowAction", production, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WorkbenchCommand", production, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FilterDag", production, StringComparison.OrdinalIgnoreCase);
    }

    private static ConvolutionPlaygroundDocument CreateDocument(RecordingCodec codec)
    {
        var converter = new ImageChannelConverter(); var convolver = new SpatialConvolver(); var combiner = new GradientCombiner();
        var processor = new ConvolutionImageProcessor(converter, convolver, combiner); var factory = new ConvolutionPresetFactory();
        return new ConvolutionPlaygroundDocument(
            new PrepareConvolutionSessionUseCase(codec, new ImageAnalysisProxyProjector()),
            new RenderConvolutionPreviewUseCase(processor, new ConvolutionDifferenceProjector()),
            new InspectConvolutionPixelUseCase(converter, new ConvolutionPixelInspector()),
            new RenderKernelResponseUseCase(new KernelFrequencyResponseAnalyzer(new(new()))),
            new RenderFullConvolutionUseCase(processor), new ExportConvolutionImageUseCase(codec, new RecordingWriter()),
            new ConvolutionKernelParser(), factory, new NullDialog(), codec, new Lifetime());
    }

    private static ConvolutionRecipe CreateRecipe() => new(new ConvolutionPresetFactory().Create("identity"),
        new(BorderMode.Replicate), new(KernelNormalizationMode.None), 0, ConvolutionChannelMode.Rgb);
    private static ConvolutionImageProcessor CreateProcessor() => new(new ImageChannelConverter(), new SpatialConvolver(), new GradientCombiner());
    private static PixelImage CreateImage() => new(new ImageSize(2, 1), [10, 20, 30, 4, 40, 50, 60, 8]);
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ImageLabPlugin.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("未找到测试仓库根目录。");
    }

    private sealed class RecordingCodec(PixelImage image) : IImageCodec
    {
        public int DecodeCalls { get; private set; } public ImageOutputFormat? LastFormat { get; private set; }
        public Task<PixelImage> DecodeAsync(string path, CancellationToken cancellationToken) { DecodeCalls++; return Task.FromResult(image.Clone()); }
        public Task<PixelImage> DecodeAsync(ReadOnlyMemory<byte> encodedImage, CancellationToken cancellationToken) => Task.FromResult(image.Clone());
        public Task<byte[]> EncodeAsync(PixelImage value, ImageOutputFormat format, int jpegQuality, CancellationToken cancellationToken)
        { LastFormat = format; return Task.FromResult<byte[]>([1, 2, 3]); }
    }
    private sealed class RecordingWriter : IAtomicFileWriter
    {
        public string? Path { get; private set; }
        public Task WriteAsync(string targetPath, ReadOnlyMemory<byte> content, CancellationToken cancellationToken) { Path = targetPath; return Task.CompletedTask; }
    }
    private sealed class NullDialog : IImageFileDialog
    {
        public Task<string?> PickImageAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task<string?> PickOutputImageAsync(string suggestedName, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }
    private sealed class Lifetime : IDocumentLifetime { public CancellationToken ClosingToken => CancellationToken.None; public bool IsClosing => false; }
}
