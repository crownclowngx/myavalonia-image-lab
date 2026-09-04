using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.Shared.ArtEffects;

namespace ImageLabPlugin.Application.ArtEffects;

internal sealed record WorkflowFileArtifact(
    string Contract,
    int Version,
    string ProducerPluginId,
    Guid ProducerOperationId,
    string Lifetime,
    string Path,
    string MediaType,
    long ByteLength,
    string Sha256);

internal sealed record ApplyArtEffectsFileRequest(
    WorkflowFileArtifact Source,
    ImageArtEffectOptions Effects,
    string OutputPath,
    Guid OutputOperationId);

internal sealed record ApplyArtEffectsFileResult(
    WorkflowFileArtifact Artifact,
    int Width,
    int Height);

/// <summary>只表达“按 Artifact 合同读取并验证一份不可变输入”的窄端口。</summary>
internal interface IWorkflowArtifactReader
{
    Task<byte[]> ReadVerifiedAsync(
        WorkflowFileArtifact artifact,
        CancellationToken cancellationToken);
}

/// <summary>只表达“不得覆盖地原子提交最终 PNG”的窄端口。</summary>
internal interface IExclusivePngCommitter
{
    Task<string> CommitAsync(
        string outputPath,
        ReadOnlyMemory<byte> png,
        CancellationToken cancellationToken);
}

internal interface IApplyArtEffectsFileUseCase
{
    Task<ApplyArtEffectsFileResult> ExecuteAsync(
        ApplyArtEffectsFileRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// 文件 Workflow 的应用用例。它只编排端口与纯领域流水线，不读取 JSON，也不包含 Host 语义。
/// </summary>
internal sealed class ApplyArtEffectsFileUseCase(
    IWorkflowArtifactReader artifactReader,
    IImageCodec imageCodec,
    ImageArtEffectPipeline pipeline,
    IExclusivePngCommitter committer) : IApplyArtEffectsFileUseCase
{
    public async Task<ApplyArtEffectsFileResult> ExecuteAsync(
        ApplyArtEffectsFileRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Source);
        ArgumentNullException.ThrowIfNull(request.Effects);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputPath);
        if (request.OutputOperationId == Guid.Empty)
        {
            throw new ArgumentException("输出操作身份不能为空。", nameof(request));
        }
        var sourcePath = Path.GetFullPath(request.Source.Path);
        var outputPath = Path.GetFullPath(request.OutputPath);
        if (string.Equals(sourcePath, outputPath, PathComparison))
        {
            throw new InvalidDataException("输出路径不能与输入 Artifact 相同。");
        }

        var encodedSource = await artifactReader
            .ReadVerifiedAsync(request.Source, cancellationToken)
            .ConfigureAwait(false);
        var source = await imageCodec
            .DecodeAsync(encodedSource, cancellationToken)
            .ConfigureAwait(false);
        if (source.Size.Width > 4096 || source.Size.Height > 4096)
        {
            throw new InvalidDataException("Workflow 艺术效果输入的单边尺寸不能超过 4096。");
        }

        var effected = pipeline.Apply(source, request.Effects, cancellationToken);
        var png = await imageCodec
            .EncodeAsync(effected, ImageOutputFormat.Png, 100, cancellationToken)
            .ConfigureAwait(false);
        var committedPath = await committer
            .CommitAsync(request.OutputPath, png, cancellationToken)
            .ConfigureAwait(false);
        var artifact = new WorkflowFileArtifact(
            WorkflowFileArtifactContract.Name,
            WorkflowFileArtifactContract.Version,
            WorkflowFileArtifactContract.ImageLabPluginId,
            request.OutputOperationId,
            WorkflowFileArtifactContract.PersistentLifetime,
            committedPath,
            WorkflowFileArtifactContract.PngMediaType,
            png.LongLength,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(png.AsSpan())));
        return new ApplyArtEffectsFileResult(
            artifact,
            effected.Size.Width,
            effected.Size.Height);
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}

/// <summary>File Artifact v1 的插件私有常量；没有 CLR 类型越过插件 ALC。</summary>
internal static class WorkflowFileArtifactContract
{
    internal const string Name = "myavalonia.workflow.file-artifact";
    internal const int Version = 1;
    internal const string ImageLabPluginId = "myavalonia.plugin.image.lab";
    internal const string PngMediaType = "image/png";
    internal const string TransientLifetime = "transient";
    internal const string RunLifetime = "run";
    internal const string PersistentLifetime = "persistent";

    internal static string RootPath => Path.Combine(
        Path.GetTempPath(),
        "MyAvaloniaManagement",
        "WorkflowArtifacts");
}
