using ImageLabPlugin.Domain.Convolution;
using ImageLabPlugin.Domain.Imaging;

namespace ImageLabPlugin.Application.Convolution;

/// <summary>拥有一次解码得到的完整源图及其分析代理；Document 关闭时统一释放会话。</summary>
/// <remarks>
/// PixelImage 由托管数组持有，无需单独 Dispose；显式生命周期用于阻止释放后的异步任务继续提交结果，
/// 并为将来增加池化缓冲保留稳定所有权边界。代理与完整图分别持有，界面不得把代理冒充完整尺寸结果。
/// </remarks>
internal sealed class ConvolutionSession : IDisposable
{
    private bool _disposed;
    public ConvolutionSession(string sourcePath, PixelImage sourceImage, PixelImage analysisProxy, int analysisMaximumEdge)
    { SourcePath = sourcePath; SourceImage = sourceImage; AnalysisProxy = analysisProxy; AnalysisMaximumEdge = analysisMaximumEdge; }
    public string SourcePath { get; }
    public PixelImage SourceImage { get; }
    public PixelImage AnalysisProxy { get; }
    public int AnalysisMaximumEdge { get; }
    public bool IsDisposed => _disposed;
    public void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(ConvolutionSession)); }
    public void Dispose() => _disposed = true;
}

internal sealed record ConvolutionPreviewResult(
    ConvolutionImageResult Convolution,
    ConvolutionDifferenceResult Difference,
    string RecipeFingerprint,
    TimeSpan Elapsed);

internal sealed record FullConvolutionResult(
    PixelImage Image,
    IReadOnlyList<ConvolutionChannelResult> Channels,
    string RecipeFingerprint,
    TimeSpan Elapsed,
    int ColorReconstructionClippedPixels);

internal sealed record ConvolutionExportResult(string OutputPath, ImageSize Size, string RecipeFingerprint);

internal interface IPrepareConvolutionSessionUseCase
{
    Task<ConvolutionSession> ExecuteAsync(string sourcePath, int analysisMaximumEdge, CancellationToken cancellationToken);
}
internal interface IRenderConvolutionPreviewUseCase
{
    Task<ConvolutionPreviewResult> ExecuteAsync(ConvolutionSession session, ConvolutionRecipe recipe, CancellationToken cancellationToken);
}
internal interface IInspectConvolutionPixelUseCase
{
    ConvolutionPixelReport Execute(ConvolutionSession session, ConvolutionPreviewResult preview, ConvolutionRecipe recipe, int x, int y);
}
internal interface IRenderKernelResponseUseCase
{
    Task<KernelFrequencyResponse> ExecuteAsync(ConvolutionRecipe recipe, CancellationToken cancellationToken);
}
internal interface IRenderFullConvolutionUseCase
{
    Task<FullConvolutionResult> ExecuteAsync(ConvolutionSession session, ConvolutionRecipe recipe, CancellationToken cancellationToken);
}
internal interface IExportConvolutionImageUseCase
{
    Task<ConvolutionExportResult> ExecuteAsync(FullConvolutionResult result, string expectedRecipeFingerprint,
        string outputPath, CancellationToken cancellationToken);
}
