using System.Diagnostics;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.Convolution;
using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Application.Convolution;

internal sealed class PrepareConvolutionSessionUseCase(IImageCodec codec, ImageAnalysisProxyProjector projector)
    : IPrepareConvolutionSessionUseCase
{
    public async Task<ConvolutionSession> ExecuteAsync(string sourcePath, int analysisMaximumEdge, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var source = await codec.DecodeAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        var proxy = await Task.Run(() => projector.Create(source, analysisMaximumEdge, cancellationToken), cancellationToken).ConfigureAwait(false);
        return new ConvolutionSession(sourcePath, source, proxy, analysisMaximumEdge);
    }
}

/// <summary>只协调代理卷积和差异，不读取文件、不创建 Bitmap，也不修改 Session。</summary>
internal sealed class RenderConvolutionPreviewUseCase(
    ConvolutionImageProcessor processor,
    ConvolutionDifferenceProjector differenceProjector) : IRenderConvolutionPreviewUseCase
{
    public Task<ConvolutionPreviewResult> ExecuteAsync(ConvolutionSession session, ConvolutionRecipe recipe, CancellationToken cancellationToken)
    {
        session.ThrowIfDisposed(); recipe.Validate();
        return Task.Run(() =>
        {
            var stopwatch = Stopwatch.StartNew();
            var convolution = processor.Process(session.AnalysisProxy, recipe, cancellationToken);
            var difference = differenceProjector.Project(session.AnalysisProxy, convolution.Image, 4, cancellationToken);
            return new ConvolutionPreviewResult(convolution, difference, recipe.Fingerprint(), stopwatch.Elapsed);
        }, cancellationToken);
    }
}

internal sealed class InspectConvolutionPixelUseCase(
    ImageChannelConverter channelConverter,
    ConvolutionPixelInspector inspector) : IInspectConvolutionPixelUseCase
{
    public ConvolutionPixelReport Execute(ConvolutionSession session, ConvolutionPreviewResult preview,
        ConvolutionRecipe recipe, int x, int y)
    {
        session.ThrowIfDisposed();
        if (!StringComparer.Ordinal.Equals(preview.RecipeFingerprint, recipe.Fingerprint()))
            throw new InvalidOperationException("探针结果已过期，请先重新生成当前配方预览。");
        var channel = recipe.Channel switch
        {
            ConvolutionChannelMode.Green => ImageChannel.Green, ConvolutionChannelMode.Blue => ImageChannel.Blue,
            ConvolutionChannelMode.Luma => ImageChannel.Luma, ConvolutionChannelMode.ChromaBlue => ImageChannel.ChromaBlue,
            ConvolutionChannelMode.ChromaRed => ImageChannel.ChromaRed, _ => ImageChannel.Red
        };
        var plane = channelConverter.Extract(session.AnalysisProxy, channel);
        if (recipe.Operator.Kind == ConvolutionOperatorKind.GradientPair && recipe.GradientOutput == GradientOutputMode.Magnitude)
            return inspector.InspectGradient(session.AnalysisProxy, preview.Convolution.Image, plane.Values.Span,
                recipe.Operator.PrimaryKernel, recipe.Operator.SecondaryKernel!, recipe.Border, recipe.Normalization,
                recipe.Bias, x, y);
        var kernel = recipe.Operator.Kind == ConvolutionOperatorKind.GradientPair && recipe.GradientOutput == GradientOutputMode.Y
            ? recipe.Operator.SecondaryKernel! : recipe.Operator.PrimaryKernel;
        return inspector.Inspect(session.AnalysisProxy, preview.Convolution.Image, plane.Values.Span, kernel,
            recipe.Border, recipe.Normalization, recipe.Bias, x, y);
    }
}

internal sealed class RenderKernelResponseUseCase(KernelFrequencyResponseAnalyzer analyzer) : IRenderKernelResponseUseCase
{
    public Task<KernelFrequencyResponse> ExecuteAsync(ConvolutionRecipe recipe, CancellationToken cancellationToken) =>
        Task.Run(() => analyzer.Analyze(recipe, cancellationToken), cancellationToken);
}

internal sealed class RenderFullConvolutionUseCase(ConvolutionImageProcessor processor) : IRenderFullConvolutionUseCase
{
    public Task<FullConvolutionResult> ExecuteAsync(ConvolutionSession session, ConvolutionRecipe recipe, CancellationToken cancellationToken)
    {
        session.ThrowIfDisposed(); recipe.Validate();
        return Task.Run(() =>
        {
            var stopwatch = Stopwatch.StartNew();
            var value = processor.Process(session.SourceImage, recipe, cancellationToken);
            return new FullConvolutionResult(value.Image, value.Channels, value.RecipeFingerprint,
                stopwatch.Elapsed, value.ColorReconstructionClippedPixels);
        }, cancellationToken);
    }
}

/// <summary>完整结果指纹必须与当前配方一致，之后才编码 PNG 并交给原子写入端口。</summary>
internal sealed class ExportConvolutionImageUseCase(IImageCodec codec, IAtomicFileWriter writer) : IExportConvolutionImageUseCase
{
    public async Task<ConvolutionExportResult> ExecuteAsync(FullConvolutionResult result, string expectedRecipeFingerprint,
        string outputPath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result); ArgumentException.ThrowIfNullOrWhiteSpace(expectedRecipeFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (!StringComparer.Ordinal.Equals(result.RecipeFingerprint, expectedRecipeFingerprint))
            throw new InvalidOperationException("完整尺寸结果已过期；请用当前参数重新执行后再导出。");
        var bytes = await codec.EncodeAsync(result.Image, ImageOutputFormat.Png, 100, cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync(outputPath, bytes, cancellationToken).ConfigureAwait(false);
        return new ConvolutionExportResult(outputPath, result.Image.Size, result.RecipeFingerprint);
    }
}
