using System.Text.Json;
using System.Text.Json.Nodes;
using ImageLabPlugin.Application.ArtEffects;
using MyAvaloniaManagement.PluginSdk;

namespace ImageLabPlugin.Infrastructure.Workflow;

/// <summary>
/// 为 ForEach 提供目录和文件名两个独立字段。沿用既有单图动作的效果及 Artifact Schema，
/// 不要求 Studio 拼接字符串，也不修改旧动作契约。它不是批处理器，单次仅处理一张图。
/// </summary>
internal static class ArtEffectDirectoryWorkflowAction
{
    internal static readonly WorkflowActionId Id =
        new("myavalonia.plugin.image.lab.workflow.apply-art-effects-file-to-directory");

    internal static WorkflowActionDescriptor CreateDescriptor()
    {
        var original = ApplyArtEffectsFileWorkflowAction.CreateDescriptor();
        var input = JsonNode.Parse(original.InputSchema.GetRawText())!.AsObject();
        var properties = input["properties"]!.AsObject();
        properties.Remove("outputPath");
        properties["outputDirectory"] = JsonNode.Parse("""{"type":"string","minLength":1,"maxLength":32767}""");
        properties["fileStem"] = JsonNode.Parse("""{"type":"string","minLength":1,"maxLength":64}""");
        input["required"] = new JsonArray("source", "blur", "bloom", "grain", "outputDirectory", "fileStem");
        return new(Id, "应用 ImageLab 艺术效果到目录", "逐项处理受验证的 PNG；仅在既有目录内创建新文件，永不覆盖。",
            JsonSerializer.SerializeToElement(input), original.OutputSchema, original.Risks, original.ConfirmationPolicy);
    }

    internal static JsonElement Adapt(JsonElement arguments)
    {
        WorkflowFileValidation.RequireProperties(arguments,
            "source", "blur", "bloom", "grain", "outputDirectory", "fileStem");
        var directory = arguments.GetProperty("outputDirectory").GetString();
        var stem = arguments.GetProperty("fileStem").GetString();
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory) || directory.Length > 32767 ||
            string.IsNullOrEmpty(stem) || stem.Length > 64 ||
            stem.Any(c => c is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '-')))
            throw new InvalidDataException("输出目录必须是绝对路径，文件名只允许 1–64 个小写字母、数字和连字符。");
        var target = ExclusivePngCommitter.ValidateTarget(Path.Combine(directory, stem + ".png"));
        // 先验证原始各对象，不能通过 JsonNode 的重新序列化把重复字段静默折叠。
        var adapted = JsonSerializer.SerializeToElement(new
        {
            source = arguments.GetProperty("source"),
            blur = arguments.GetProperty("blur"),
            bloom = arguments.GetProperty("bloom"),
            grain = arguments.GetProperty("grain"),
            outputPath = target
        });
        ApplyArtEffectsFileWorkflowActionHandler.ValidateArguments(adapted);
        return adapted;
    }
}

/// <summary>复用单图 Handler 的处理与最终提交顺序；没有 Gateway，不能嵌套调用其他 Provider。</summary>
internal sealed class ArtEffectDirectoryWorkflowActionHandler(
    IApplyArtEffectsFileUseCase useCase, IExclusivePngCommitter committer) : IWorkflowActionHandler
{
    public ValueTask<JsonElement> InvokeAsync(JsonElement arguments, WorkflowActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ApplyArtEffectsFileWorkflowActionHandler(useCase, committer)
            .InvokeAsync(ArtEffectDirectoryWorkflowAction.Adapt(arguments), context, cancellationToken);
    }
}
