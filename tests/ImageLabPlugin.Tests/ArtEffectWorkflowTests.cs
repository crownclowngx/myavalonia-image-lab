using System.Security.Cryptography;
using System.Text.Json;
using ImageLabPlugin.Application.ArtEffects;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.Shared.ArtEffects;
using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Infrastructure.Imaging;
using ImageLabPlugin.Infrastructure.Workflow;
using MyAvaloniaManagement.PluginSdk;
using Xunit;

namespace ImageLabPlugin.Tests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ArtEffectWorkflowTests
{
    [Fact]
    public void SharedDomain不依赖UI工作流JsonDI或文件系统()
    {
        var root = FindRepositoryRoot();
        var source = string.Join('\n', Directory.EnumerateFiles(
                Path.Combine(root, "src", "ImageLabPlugin.Plugin", "Domain", "Shared", "ArtEffects"),
                "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText));

        Assert.DoesNotContain("using Avalonia", source, StringComparison.Ordinal);
        Assert.DoesNotContain("using MyAvaloniaManagement", source, StringComparison.Ordinal);
        Assert.DoesNotContain("using System.Text.Json", source, StringComparison.Ordinal);
        Assert.DoesNotContain("using Microsoft.Extensions", source, StringComparison.Ordinal);
        Assert.DoesNotContain("using System.IO", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IServiceProvider", source, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Reflection", source, StringComparison.Ordinal);
    }

    [Fact]
    public void 固定流水线保持输入和Alpha且同Seed逐字节确定()
    {
        var input = new PixelImage(
            new ImageSize(3, 2),
            [20, 40, 60, 11, 240, 220, 200, 22, 80, 100, 120, 33,
             10, 30, 50, 44, 200, 180, 160, 55, 90, 110, 130, 66]);
        var original = input.Rgba.ToArray();
        var pipeline = Pipeline();
        var options = new ImageArtEffectOptions(
            new BlurEffectSettings(true, 0.8),
            new BloomEffectSettings(true, 0.5, 1.2, 0.7),
            new GrainEffectSettings(true, 4, -12));

        var first = pipeline.Apply(input, options);
        var second = pipeline.Apply(input, options);

        Assert.Equal(original, input.Rgba.ToArray());
        Assert.Equal(first.Rgba.ToArray(), second.Rgba.ToArray());
        for (var offset = 3; offset < original.Length; offset += 4)
        {
            Assert.Equal(original[offset], first.Rgba.Span[offset]);
        }
    }

    [Fact]
    public void 禁用和零强度均为恒等且不同GrainSeed产生不同结果()
    {
        var input = new PixelImage(new ImageSize(2, 2),
            [100, 100, 100, 255, 110, 110, 110, 254,
             120, 120, 120, 253, 130, 130, 130, 252]);
        var pipeline = Pipeline();
        var identity = new ImageArtEffectOptions(
            new BlurEffectSettings(false, 3),
            new BloomEffectSettings(true, 0.5, 1, 0),
            new GrainEffectSettings(true, 0, 1));
        var grainA = identity with { Grain = new GrainEffectSettings(true, 10, 1) };
        var grainB = identity with { Grain = new GrainEffectSettings(true, 10, 2) };

        Assert.Equal(input.Rgba.ToArray(), pipeline.Apply(input, identity).Rgba.ToArray());
        Assert.NotEqual(pipeline.Apply(input, grainA).Rgba.ToArray(),
            pipeline.Apply(input, grainB).Rgba.ToArray());
    }

    [Fact]
    public void Bloom只从阈值以上像素扩散并保持Alpha()
    {
        var input = new PixelImage(new ImageSize(3, 1),
            [0, 0, 0, 10, 255, 255, 255, 20, 0, 0, 0, 30]);
        var processor = new BloomArtEffectProcessor();
        var options = new ImageArtEffectOptions(
            new BlurEffectSettings(false, 0),
            new BloomEffectSettings(true, 0.9, 0.5, 1),
            new GrainEffectSettings(false, 0, 0));

        var result = processor.Apply(input, options, CancellationToken.None);

        Assert.True(result.GetPixel(0, 0).R > 0);
        Assert.Equal((byte)255, result.GetPixel(1, 0).R);
        Assert.Equal(new byte[] { 10, 20, 30 },
            new[] { result.GetAlpha(0, 0), result.GetAlpha(1, 0), result.GetAlpha(2, 0) });
    }

    [Fact]
    public async Task 真实Artifact经Action生成持久PNG且不删除输入()
    {
        var producerId = "myavalonia.plugin.test.art";
        var operationId = Guid.NewGuid();
        var operationRoot = Path.Combine(
            WorkflowFileArtifactContract.RootPath,
            producerId,
            operationId.ToString("D"));
        var outputRoot = Path.Combine(Path.GetTempPath(), "ImageLabG0007Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(operationRoot);
        Directory.CreateDirectory(outputRoot);
        try
        {
            var codec = new AvaloniaImageCodec();
            var inputImage = new PixelImage(new ImageSize(2, 1),
                [20, 40, 60, 70, 200, 180, 160, 150]);
            var png = await codec.EncodeAsync(inputImage, ImageOutputFormat.Png, 100, default);
            var sourcePath = Path.Combine(operationRoot, "source.png");
            await File.WriteAllBytesAsync(sourcePath, png);
            await File.WriteAllTextAsync(Path.Combine(operationRoot, ".owner.json"),
                JsonSerializer.Serialize(new
                {
                    contract = WorkflowFileArtifactContract.Name,
                    version = WorkflowFileArtifactContract.Version,
                    producerPluginId = producerId,
                    producerOperationId = operationId,
                    createdAtUtc = DateTimeOffset.UtcNow,
                }));
            var outputPath = Path.Combine(outputRoot, "effected.png");
            var input = JsonSerializer.SerializeToElement(new
            {
                source = new
                {
                    contract = WorkflowFileArtifactContract.Name,
                    version = 1,
                    producerPluginId = producerId,
                    producerOperationId = operationId.ToString("D"),
                    lifetime = "transient",
                    path = sourcePath,
                    mediaType = "image/png",
                    byteLength = png.LongLength,
                    sha256 = Convert.ToHexString(SHA256.HashData(png)),
                },
                blur = new { enabled = false, sigma = 0d },
                bloom = new { enabled = false, threshold = 0.72, sigma = 5d, strength = 0.8 },
                grain = new { enabled = true, amount = 3d, seed = 42L },
                outputPath,
            });
            var useCase = new ApplyArtEffectsFileUseCase(
                new WorkflowArtifactReader(), codec, Pipeline(), new ExclusivePngCommitter());
            var handler = new ApplyArtEffectsFileWorkflowActionHandler(useCase);

            var result = await handler.InvokeAsync(input,
                new WorkflowActionContext(
                    Guid.NewGuid(),
                    new PluginId("myavalonia.plugin.test.consumer"),
                    new Progress<WorkflowActionProgress>()),
                CancellationToken.None);

            Assert.True(File.Exists(sourcePath));
            Assert.True(File.Exists(outputPath));
            Assert.Equal("persistent", result.GetProperty("artifact").GetProperty("lifetime").GetString());
            Assert.Equal(2, result.GetProperty("image").GetProperty("width").GetInt32());
        }
        finally
        {
            if (Directory.Exists(operationRoot)) Directory.Delete(operationRoot, recursive: true);
            if (Directory.Exists(outputRoot)) Directory.Delete(outputRoot, recursive: true);
        }
    }

    [Fact]
    public void Action描述符冻结身份风险和Schema()
    {
        var descriptor = ApplyArtEffectsFileWorkflowAction.CreateDescriptor();

        Assert.Equal("myavalonia.plugin.image.lab.workflow.apply-art-effects-file", descriptor.Id.Value);
        Assert.Equal(WorkflowActionConfirmationPolicy.OncePerRun, descriptor.ConfirmationPolicy);
        Assert.True(descriptor.Risks.HasFlag(WorkflowActionRiskFlags.ReadsLocalFiles));
        Assert.True(descriptor.Risks.HasFlag(WorkflowActionRiskFlags.WritesLocalFiles));
        Assert.True(descriptor.Risks.HasFlag(WorkflowActionRiskFlags.LongRunning));
        Assert.Contains("source", descriptor.InputSchema.GetProperty("required")
            .EnumerateArray().Select(value => value.GetString()));
    }

    [Fact]
    public async Task 输出冲突和预取消都不覆盖文件也不遗留Partial()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"image-lab-commit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, "result.png");
        var committer = new ExclusivePngCommitter();
        try
        {
            await File.WriteAllBytesAsync(target, [1, 2, 3]);
            await Assert.ThrowsAsync<IOException>(() =>
                committer.CommitAsync(target, new byte[] { 4, 5, 6 }, CancellationToken.None));
            Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(target));

            File.Delete(target);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                committer.CommitAsync(target, new byte[1024], cancellation.Token));
            Assert.False(File.Exists(target));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.partial"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ImageArtEffectPipeline Pipeline() => new(
        new GaussianBlurArtEffectProcessor(),
        new BloomArtEffectProcessor(),
        new GrainArtEffectProcessor());

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ImageLabPlugin.slnx")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("找不到 ImageLab 仓库根目录。");
    }
}
