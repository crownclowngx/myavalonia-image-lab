using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using ImageLabPlugin.Application.ArtEffects;
using ImageLabPlugin.Domain.Shared.ArtEffects;
using MyAvaloniaManagement.PluginSdk;

namespace ImageLabPlugin.Infrastructure.Workflow;

/// <summary>读取 File Artifact v1，并在解码前完成路径、所有权、长度与摘要验证。</summary>
internal sealed class WorkflowArtifactReader : IWorkflowArtifactReader
{
    private const int MaximumOwnerMarkerBytes = 4096;
    private const int MaximumEncodedBytes = 256 * 1024 * 1024;

    public async Task<byte[]> ReadVerifiedAsync(
        WorkflowFileArtifact artifact,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateShape(artifact);
        var operationRoot = BuildOperationRoot(artifact);
        var expectedPath = Path.Combine(operationRoot, "source.png");
        var actualPath = Path.GetFullPath(artifact.Path);
        if (!string.Equals(expectedPath, actualPath, PathComparison))
        {
            throw new InvalidDataException("Artifact 路径与生产者操作目录不匹配。");
        }

        WorkflowFileValidation.RejectReparseAncestors(actualPath);
        WorkflowFileValidation.RejectReparseAncestors(Path.Combine(operationRoot, ".owner.json"));
        await ValidateOwnerMarkerAsync(operationRoot, artifact, cancellationToken).ConfigureAwait(false);
        var information = new FileInfo(actualPath);
        if (!information.Exists || information.Length != artifact.ByteLength ||
            information.Length is < 8 or > MaximumEncodedBytes)
        {
            throw new InvalidDataException("Artifact 文件不存在，或长度未通过验证。");
        }

        var bytes = await WorkflowFileValidation.ReadBoundedAsync(actualPath,
            checked((int)artifact.ByteLength), cancellationToken).ConfigureAwait(false);
        if (bytes.LongLength != artifact.ByteLength)
            throw new InvalidDataException("Artifact 实际读取长度发生变化。");
        WorkflowFileValidation.ValidatePngHeader(bytes);
        var actualHash = Convert.ToHexString(SHA256.HashData(bytes));
        if (!string.Equals(actualHash, artifact.Sha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Artifact SHA-256 未通过验证。");
        }
        return bytes;
    }

    private static void ValidateShape(WorkflowFileArtifact artifact)
    {
        if (artifact.Contract != WorkflowFileArtifactContract.Name ||
            artifact.Version != WorkflowFileArtifactContract.Version ||
            artifact.ProducerOperationId == Guid.Empty ||
            artifact.MediaType != WorkflowFileArtifactContract.PngMediaType ||
            artifact.Lifetime is not (WorkflowFileArtifactContract.TransientLifetime or
                WorkflowFileArtifactContract.RunLifetime) ||
            artifact.ByteLength is < 8 or > MaximumEncodedBytes ||
            artifact.Sha256 is null || artifact.Sha256.Length != 64 ||
            artifact.Sha256.Any(character => !Uri.IsHexDigit(character) || char.IsLower(character)))
        {
            throw new InvalidDataException("File Artifact v1 字段未通过验证。");
        }
        _ = new PluginId(artifact.ProducerPluginId);
        if (!Path.IsPathFullyQualified(artifact.Path))
        {
            throw new InvalidDataException("Artifact 必须使用绝对路径。");
        }
    }

    private static string BuildOperationRoot(WorkflowFileArtifact artifact)
    {
        var producerRoot = Path.GetFullPath(Path.Combine(
            WorkflowFileArtifactContract.RootPath,
            artifact.ProducerPluginId));
        var operationRoot = Path.GetFullPath(Path.Combine(
            producerRoot,
            artifact.ProducerOperationId.ToString("D")));
        if (!IsWithin(producerRoot, operationRoot))
        {
            throw new InvalidDataException("Artifact 操作目录越界。");
        }
        return operationRoot;
    }

    private static async Task ValidateOwnerMarkerAsync(
        string operationRoot,
        WorkflowFileArtifact artifact,
        CancellationToken cancellationToken)
    {
        var markerPath = Path.Combine(operationRoot, ".owner.json");
        var markerInfo = new FileInfo(markerPath);
        if (!markerInfo.Exists || markerInfo.Length is <= 0 or > MaximumOwnerMarkerBytes)
        {
            throw new InvalidDataException("Artifact 所有权标记不存在或超限。");
        }
        var markerBytes = await WorkflowFileValidation.ReadBoundedAsync(markerPath,
            MaximumOwnerMarkerBytes, cancellationToken).ConfigureAwait(false);
        using var markerJson = JsonDocument.Parse(markerBytes);
        var markerNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in markerJson.RootElement.EnumerateObject())
            if (!markerNames.Add(property.Name) || property.Name is not
                ("contract" or "version" or "producerPluginId" or "producerOperationId" or "createdAtUtc" or "invocationId" or "itemId"))
                throw new InvalidDataException("所有权标记包含未知或重复字段。");
        var marker = JsonSerializer.Deserialize<OwnerMarker>(markerBytes) ??
                     throw new InvalidDataException("Artifact 所有权标记无法解析。");
        if (marker.Contract != WorkflowFileArtifactContract.Name ||
            marker.Version != WorkflowFileArtifactContract.Version ||
            marker.ProducerPluginId != artifact.ProducerPluginId ||
            marker.ProducerOperationId != artifact.ProducerOperationId ||
            marker.CreatedAtUtc == default ||
            marker.CreatedAtUtc > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            throw new InvalidDataException("Artifact 所有权标记不匹配。");
        }
    }

    private static bool IsWithin(string root, string candidate)
    {
        var relative = Path.GetRelativePath(root, candidate);
        return relative != ".." &&
               !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
               !Path.IsPathFullyQualified(relative);
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private sealed record OwnerMarker(
        [property: JsonPropertyName("contract")] string Contract,
        [property: JsonPropertyName("version")] int Version,
        [property: JsonPropertyName("producerPluginId")] string ProducerPluginId,
        [property: JsonPropertyName("producerOperationId")] Guid ProducerOperationId,
        [property: JsonPropertyName("createdAtUtc")] DateTimeOffset CreatedAtUtc);
}

/// <summary>在用户已经存在的目录中创建最终 PNG；冲突时失败，永不覆盖旧文件。</summary>
internal sealed class ExclusivePngCommitter : IExclusivePngCommitter
{
    /// <summary>无副作用预检；提交前仍重复检查，不能把预检当作独占文件系统的承诺。</summary>
    internal static string ValidateTarget(string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath) || outputPath.Length > 32767 ||
            !Path.IsPathFullyQualified(outputPath) ||
            !string.Equals(Path.GetExtension(outputPath), ".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("输出必须是绝对 PNG 路径。");
        var target = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(target)!;
        if (!Directory.Exists(directory) || IsWithin(WorkflowFileArtifactContract.RootPath, target))
            throw new InvalidDataException("输出目录不存在，或输出位于 Workflow 临时目录中。");
        WorkflowFileValidation.RejectReparseAncestors(directory);
        if (Path.Exists(target)) throw new IOException("输出文件已经存在，不允许覆盖。");
        return target;
    }

    public async Task<string> CommitAsync(
        string outputPath,
        ReadOnlyMemory<byte> png,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = ValidateTarget(outputPath);
        var directory = Path.GetDirectoryName(target)!;

        var temporary = Path.Combine(directory, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.partial");
        var committed = false;
        try
        {
            await using (var stream = new FileStream(
                             temporary,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             64 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(png, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            ValidateTarget(target);
            File.Move(temporary, target, overwrite: false);
            committed = true;
            return target;
        }
        finally
        {
            if (!committed && File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static bool IsWithin(string root, string candidate)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), candidate);
        return relative != ".." &&
               !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
               !Path.IsPathFullyQualified(relative);
    }
}

internal static class ApplyArtEffectsFileWorkflowAction
{
    internal static readonly WorkflowActionId Id =
        new("myavalonia.plugin.image.lab.workflow.apply-art-effects-file");

    internal static WorkflowActionDescriptor CreateDescriptor()
    {
        using var inputSchema = JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "source": {
                  "type": "object",
                  "properties": {
                    "contract": { "type": "string", "enum": ["myavalonia.workflow.file-artifact"] },
                    "version": { "type": "integer", "enum": [1] },
                    "producerPluginId": { "type": "string", "minLength": 1, "maxLength": 128 },
                    "producerOperationId": { "type": "string", "minLength": 36, "maxLength": 36 },
                    "lifetime": { "type": "string", "enum": ["transient", "run"] },
                    "path": { "type": "string", "minLength": 1, "maxLength": 32767 },
                    "mediaType": { "type": "string", "enum": ["image/png"] },
                    "byteLength": { "type": "integer", "minimum": 1, "maximum": 268435456 },
                    "sha256": { "type": "string", "minLength": 64, "maxLength": 64 }
                  },
                  "required": ["contract", "version", "producerPluginId", "producerOperationId", "lifetime", "path", "mediaType", "byteLength", "sha256"],
                  "additionalProperties": false
                },
                "blur": {
                  "type": "object",
                  "properties": { "enabled": { "type": "boolean" }, "sigma": { "type": "number", "minimum": 0, "maximum": 10 } },
                  "required": ["enabled", "sigma"], "additionalProperties": false
                },
                "bloom": {
                  "type": "object",
                  "properties": {
                    "enabled": { "type": "boolean" }, "threshold": { "type": "number", "minimum": 0, "maximum": 1 },
                    "sigma": { "type": "number", "minimum": 0.1, "maximum": 10 }, "strength": { "type": "number", "minimum": 0, "maximum": 4 }
                  },
                  "required": ["enabled", "threshold", "sigma", "strength"], "additionalProperties": false
                },
                "grain": {
                  "type": "object",
                  "properties": { "enabled": { "type": "boolean" }, "amount": { "type": "number", "minimum": 0, "maximum": 100 }, "seed": { "type": "integer" } },
                  "required": ["enabled", "amount", "seed"], "additionalProperties": false
                },
                "outputPath": { "type": "string", "minLength": 1, "maxLength": 32767 }
              },
              "required": ["source", "blur", "bloom", "grain", "outputPath"],
              "additionalProperties": false
            }
            """);
        using var outputSchema = JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "artifact": {
                  "type": "object",
                  "properties": {
                    "contract": { "type": "string", "enum": ["myavalonia.workflow.file-artifact"] },
                    "version": { "type": "integer", "enum": [1] },
                    "producerPluginId": { "type": "string", "enum": ["myavalonia.plugin.image.lab"] },
                    "producerOperationId": { "type": "string", "minLength": 36, "maxLength": 36 },
                    "lifetime": { "type": "string", "enum": ["persistent"] },
                    "path": { "type": "string", "minLength": 1, "maxLength": 32767 },
                    "mediaType": { "type": "string", "enum": ["image/png"] },
                    "byteLength": { "type": "integer", "minimum": 1, "maximum": 268435456 },
                    "sha256": { "type": "string", "minLength": 64, "maxLength": 64 }
                  },
                  "required": ["contract", "version", "producerPluginId", "producerOperationId", "lifetime", "path", "mediaType", "byteLength", "sha256"],
                  "additionalProperties": false
                },
                "image": {
                  "type": "object",
                  "properties": {
                    "width": { "type": "integer", "minimum": 1, "maximum": 4096 },
                    "height": { "type": "integer", "minimum": 1, "maximum": 4096 }
                  },
                  "required": ["width", "height"], "additionalProperties": false
                }
              },
              "required": ["artifact", "image"], "additionalProperties": false
            }
            """);
        return new WorkflowActionDescriptor(
            Id,
            "应用 ImageLab 艺术效果",
            "从受验证的 PNG Artifact 读取图像，按 Blur、Bloom、Grain 固定顺序处理并创建新 PNG。",
            inputSchema.RootElement,
            outputSchema.RootElement,
            WorkflowActionRiskFlags.ReadsLocalFiles |
            WorkflowActionRiskFlags.WritesLocalFiles |
            WorkflowActionRiskFlags.LongRunning,
            WorkflowActionConfirmationPolicy.OncePerRun);
    }
}

internal sealed class ApplyArtEffectsFileWorkflowActionHandler(
    IApplyArtEffectsFileUseCase useCase,
    IExclusivePngCommitter committer) : IWorkflowActionHandler
{
    public async ValueTask<JsonElement> InvokeAsync(
        JsonElement arguments,
        WorkflowActionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateArguments(arguments);
        var input = arguments.Deserialize<ActionArguments>() ??
                    throw new ArgumentException("ImageLab 艺术效果参数无法解析。", nameof(arguments));
        context.Progress.Report(new WorkflowActionProgress("validating", 5, "正在验证输入文件。"));
        var request = input.ToRequest(context.InvocationId);
        request.Effects.Validate();
        ExclusivePngCommitter.ValidateTarget(request.OutputPath);
        context.Progress.Report(new WorkflowActionProgress("processing", 20, "正在应用艺术效果。"));
        var prepared = await useCase.PrepareAsync(request, cancellationToken).ConfigureAwait(false);
        var result = prepared.Result;
        var output = JsonSerializer.SerializeToElement(new
        {
            artifact = ToJson(result.Artifact),
            image = new { width = result.Width, height = result.Height },
        });
        // 所有可能失败的序列化、通知与取消检查均在提交之前。原子改名后直接移交预构造响应，
        // 不再用迟到取消把已提交文件报告为本地失败；Host 最终拒绝仍属于协议无法消除的窗口。
        context.Progress.Report(new WorkflowActionProgress("committing", 95, "正在提交新的 PNG。"));
        cancellationToken.ThrowIfCancellationRequested();
        await committer.CommitAsync(result.Artifact.Path, prepared.Png, cancellationToken).ConfigureAwait(false);
        return output;
    }

    internal static void ValidateArguments(JsonElement arguments)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(arguments.GetRawText()) > 256 * 1024)
            throw new InvalidDataException("参数超过 Workflow 输入预算。");
        WorkflowFileValidation.RequireProperties(arguments, "source", "blur", "bloom", "grain", "outputPath");
        WorkflowFileValidation.RequireProperties(arguments.GetProperty("source"), "contract", "version",
            "producerPluginId", "producerOperationId", "lifetime", "path", "mediaType", "byteLength", "sha256");
        WorkflowFileValidation.RequireProperties(arguments.GetProperty("blur"), "enabled", "sigma");
        WorkflowFileValidation.RequireProperties(arguments.GetProperty("bloom"), "enabled", "threshold", "sigma", "strength");
        WorkflowFileValidation.RequireProperties(arguments.GetProperty("grain"), "enabled", "amount", "seed");
    }

    internal static object ToJson(WorkflowFileArtifact artifact) => new
    {
        contract = artifact.Contract,
        version = artifact.Version,
        producerPluginId = artifact.ProducerPluginId,
        producerOperationId = artifact.ProducerOperationId.ToString("D"),
        lifetime = artifact.Lifetime,
        path = artifact.Path,
        mediaType = artifact.MediaType,
        byteLength = artifact.ByteLength,
        sha256 = artifact.Sha256,
    };

    private sealed record ActionArguments(
        [property: JsonPropertyName("source")] ArtifactArguments Source,
        [property: JsonPropertyName("blur")] BlurArguments Blur,
        [property: JsonPropertyName("bloom")] BloomArguments Bloom,
        [property: JsonPropertyName("grain")] GrainArguments Grain,
        [property: JsonPropertyName("outputPath")] string OutputPath)
    {
        internal ApplyArtEffectsFileRequest ToRequest(Guid outputOperationId) => new(
            Source.ToArtifact(),
            new ImageArtEffectOptions(
                new BlurEffectSettings(Blur.Enabled, Blur.Sigma),
                new BloomEffectSettings(Bloom.Enabled, Bloom.Threshold, Bloom.Sigma, Bloom.Strength),
                new GrainEffectSettings(Grain.Enabled, Grain.Amount, Grain.Seed)),
            OutputPath,
            outputOperationId);
    }

    private sealed record ArtifactArguments(
        [property: JsonPropertyName("contract")] string Contract,
        [property: JsonPropertyName("version")] int Version,
        [property: JsonPropertyName("producerPluginId")] string ProducerPluginId,
        [property: JsonPropertyName("producerOperationId")] Guid ProducerOperationId,
        [property: JsonPropertyName("lifetime")] string Lifetime,
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("mediaType")] string MediaType,
        [property: JsonPropertyName("byteLength")] long ByteLength,
        [property: JsonPropertyName("sha256")] string Sha256)
    {
        internal WorkflowFileArtifact ToArtifact() => new(
            Contract, Version, ProducerPluginId, ProducerOperationId, Lifetime,
            Path, MediaType, ByteLength, Sha256);
    }

    private sealed record BlurArguments(
        [property: JsonPropertyName("enabled")] bool Enabled,
        [property: JsonPropertyName("sigma")] double Sigma);

    private sealed record BloomArguments(
        [property: JsonPropertyName("enabled")] bool Enabled,
        [property: JsonPropertyName("threshold")] double Threshold,
        [property: JsonPropertyName("sigma")] double Sigma,
        [property: JsonPropertyName("strength")] double Strength);

    private sealed record GrainArguments(
        [property: JsonPropertyName("enabled")] bool Enabled,
        [property: JsonPropertyName("amount")] double Amount,
        [property: JsonPropertyName("seed")] long Seed);
}
