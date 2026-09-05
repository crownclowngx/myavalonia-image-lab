using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using ImageLabPlugin.Application.ArtEffects;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.Shared.ArtEffects;
using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Infrastructure.Imaging;
using ImageLabPlugin.Infrastructure.Workflow;
using MyAvaloniaManagement.PluginSdk;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ImageLabPlugin.Tests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class G0013WorkflowBoundaryTests
{
    [Fact]
    public void Standalone真实Module可注册两个ScopedHandler且不请求HostGateway()
    {
        var registration = new ImageLabPlugin.Standalone.PreviewPluginRegistration();
        new ImageLabPlugin.Plugin.ImageLabPluginModule().Configure(registration);
        Assert.Equal(2, registration.Services.Count(service => typeof(IWorkflowActionHandler).IsAssignableFrom(service.ServiceType)));
        using var provider = registration.Services.BuildServiceProvider(validateScopes: true);
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        Assert.NotSame(first.ServiceProvider.GetRequiredService<ArtEffectDirectoryWorkflowActionHandler>(),
            second.ServiceProvider.GetRequiredService<ArtEffectDirectoryWorkflowActionHandler>());
        Assert.Throws<NotSupportedException>(registration.UseWorkflowActionGateway);
    }

    [Fact]
    public void 旧Action输入输出与G0007固定夹具兼容且新动作复用输出和风险()
    {
        var old = ApplyArtEffectsFileWorkflowAction.CreateDescriptor();
        var directory = ArtEffectDirectoryWorkflowAction.CreateDescriptor();
        foreach (var (name, current) in new[] { ("inputSchema", old.InputSchema), ("outputSchema", old.OutputSchema) })
        {
            using var stream = typeof(G0013WorkflowBoundaryTests).Assembly.GetManifestResourceStream(
                $"ImageLabPlugin.Tests.Fixtures.G0013.g0007-{name}.json")!;
            using var fixture = JsonDocument.Parse(stream);
            Assert.True(JsonElement.DeepEquals(fixture.RootElement, current));
        }
        Assert.Equal(old.Risks, directory.Risks);
        Assert.Equal(WorkflowActionConfirmationPolicy.OncePerRun, directory.ConfirmationPolicy);
        Assert.True(JsonElement.DeepEquals(old.OutputSchema, directory.OutputSchema));
    }

    [Fact]
    public async Task 目录Action真实PNG成功且没有提交后的可失败通知()
    {
        using var files = await Files.CreateAsync();
        var stages = new List<string>();
        var result = await files.Handler.InvokeAsync(files.Arguments(), Context(new InlineProgress(p => stages.Add(p.Stage))), default);
        Assert.Equal("persistent", result.GetProperty("artifact").GetProperty("lifetime").GetString());
        Assert.True(File.Exists(files.OutputPath));
        Assert.Equal(files.Png, await File.ReadAllBytesAsync(files.Source.Path));
        Assert.Equal("committing", stages.Last());
        Assert.Equal(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(files.OutputPath))),
            result.GetProperty("artifact").GetProperty("sha256").GetString());
        Assert.Empty(Directory.GetFiles(files.OutputRoot, "*.partial"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../outside")]
    [InlineData("UPPER")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a:b")]
    [InlineData("with space")]
    [InlineData("result.png")]
    public async Task 非法文件名不产生输出(string stem)
    {
        using var files = await Files.CreateAsync();
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await files.Handler.InvokeAsync(files.Arguments(stem), Context(), default));
        Assert.Empty(Directory.GetFiles(files.OutputRoot));
    }

    [Theory]
    [InlineData("validating")]
    [InlineData("processing")]
    [InlineData("committing")]
    public async Task 各阶段取消保留输入且不提交目标(string stage)
    {
        using var files = await Files.CreateAsync();
        using var cts = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await files.Handler.InvokeAsync(files.Arguments(),
            Context(new InlineProgress(p => { if (p.Stage == stage) cts.Cancel(); })), cts.Token));
        Assert.False(File.Exists(files.OutputPath));
        Assert.Equal(files.Png, await File.ReadAllBytesAsync(files.Source.Path));
        Assert.Empty(Directory.GetFiles(files.OutputRoot));
    }

    [Theory]
    [InlineData("source-extra")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("null")]
    [InlineData("range")]
    public async Task 严格参数校验先于输出(string scenario)
    {
        using var files = await Files.CreateAsync();
        var node = JsonNode.Parse(files.Arguments().GetRawText())!;
        if (scenario == "source-extra") node["source"]!["callerId"] = "fake";
        if (scenario == "missing") node.AsObject().Remove("blur");
        if (scenario == "null") node["bloom"] = null;
        if (scenario == "range") node["blur"]!["sigma"] = 11;
        var text = node.ToJsonString();
        if (scenario == "duplicate") text = text.Replace("\"sigma\":1", "\"sigma\":1,\"sigma\":2", StringComparison.Ordinal);
        using var json = JsonDocument.Parse(text);
        await Assert.ThrowsAnyAsync<Exception>(async () => await files.Handler.InvokeAsync(json.RootElement, Context(), default));
        Assert.False(File.Exists(files.OutputPath));
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("length")]
    [InlineData("version")]
    [InlineData("operation")]
    [InlineData("marker-missing")]
    [InlineData("marker-large")]
    [InlineData("marker-duplicate")]
    [InlineData("png-large")]
    public async Task 损坏Artifact在处理前被拒绝(string scenario)
    {
        using var files = await Files.CreateAsync();
        var source = files.Source;
        if (scenario == "hash") source = source with { Sha256 = new string('0', 64) };
        if (scenario == "length") source = source with { ByteLength = source.ByteLength + 1 };
        if (scenario == "version") source = source with { Version = 2 };
        if (scenario == "operation") source = source with { ProducerOperationId = Guid.NewGuid() };
        if (scenario == "marker-missing") File.Delete(files.MarkerPath);
        if (scenario == "marker-large") await File.WriteAllTextAsync(files.MarkerPath, new string(' ', 4097));
        if (scenario == "marker-duplicate") await File.WriteAllTextAsync(files.MarkerPath, "{\"version\":1,\"version\":1}");
        if (scenario == "png-large")
        {
            var bytes = files.Png.ToArray();
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16, 4), 4097);
            await File.WriteAllBytesAsync(source.Path, bytes);
            source = source with { Sha256 = Convert.ToHexString(SHA256.HashData(bytes)) };
        }
        await Assert.ThrowsAnyAsync<Exception>(() => new WorkflowArtifactReader().ReadVerifiedAsync(source, default));
        Assert.False(File.Exists(files.OutputPath));
    }

    [Fact]
    public async Task 已有输出与临时目录均拒绝且输入保持不变()
    {
        using var files = await Files.CreateAsync();
        await File.WriteAllTextAsync(files.OutputPath, "original");
        await Assert.ThrowsAsync<IOException>(async () => await files.Handler.InvokeAsync(files.Arguments(), Context(), default));
        Assert.Equal("original", await File.ReadAllTextAsync(files.OutputPath));
        var node = JsonNode.Parse(files.Arguments().GetRawText())!;
        node["outputDirectory"] = files.SourceRoot;
        await Assert.ThrowsAsync<InvalidDataException>(async () => await files.Handler.InvokeAsync(JsonSerializer.SerializeToElement(node), Context(), default));
        Assert.Equal(files.Png, await File.ReadAllBytesAsync(files.Source.Path));
    }

    [Fact]
    public async Task 实际读取预算边界与祖先Junction均验证()
    {
        using var files = await Files.CreateAsync();
        Assert.Equal(files.Png, await WorkflowFileValidation.ReadBoundedAsync(files.Source.Path, files.Png.Length, default));
        await Assert.ThrowsAsync<InvalidDataException>(() => WorkflowFileValidation.ReadBoundedAsync(files.Source.Path, files.Png.Length - 1, default));
        var link = Path.Combine(files.OutputRoot, "alias");
        if (OperatingSystem.IsWindows())
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
            { Arguments = $"/c mklink /J \"{link}\" \"{files.SourceRoot}\"", CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true });
            await process!.WaitForExitAsync(); Assert.Equal(0, process.ExitCode);
        }
        else Directory.CreateSymbolicLink(link, files.SourceRoot);
        try { Assert.Throws<InvalidDataException>(() => WorkflowFileValidation.RejectReparseAncestors(Path.Combine(link, "source.png"))); }
        finally { Directory.Delete(link); }
        Assert.True(File.Exists(files.Source.Path));
    }

    private static WorkflowActionContext Context(IProgress<WorkflowActionProgress>? progress = null) =>
        new(Guid.NewGuid(), new PluginId("myavalonia.plugin.test.consumer"), progress ?? new InlineProgress(_ => { }));
    private sealed class InlineProgress(Action<WorkflowActionProgress> report) : IProgress<WorkflowActionProgress>
    { public void Report(WorkflowActionProgress value) => report(value); }

    private sealed class Files : IDisposable
    {
        public string OutputRoot { get; } = Path.Combine(Path.GetTempPath(), "g0013-image-tests", Guid.NewGuid().ToString("N"));
        public string SourceRoot { get; private set; } = "";
        public string MarkerPath => Path.Combine(SourceRoot, ".owner.json");
        public string OutputPath => Path.Combine(OutputRoot, "result.png");
        public byte[] Png { get; private set; } = [];
        public WorkflowFileArtifact Source { get; private set; } = null!;
        public ArtEffectDirectoryWorkflowActionHandler Handler { get; private set; } = null!;
        public static async Task<Files> CreateAsync()
        {
            var files = new Files();
            var id = Guid.NewGuid();
            const string producer = "myavalonia.plugin.test.g0013";
            files.SourceRoot = Path.Combine(WorkflowFileArtifactContract.RootPath, producer, id.ToString("D"));
            Directory.CreateDirectory(files.SourceRoot); Directory.CreateDirectory(files.OutputRoot);
            var codec = new AvaloniaImageCodec();
            files.Png = await codec.EncodeAsync(new PixelImage(new ImageSize(2, 1), [30, 40, 50, 80, 180, 190, 200, 255]), ImageOutputFormat.Png, 100, default);
            var sourcePath = Path.Combine(files.SourceRoot, "source.png");
            await File.WriteAllBytesAsync(sourcePath, files.Png);
            await File.WriteAllTextAsync(files.MarkerPath, JsonSerializer.Serialize(new
            { contract = WorkflowFileArtifactContract.Name, version = 1, producerPluginId = producer, producerOperationId = id, createdAtUtc = DateTimeOffset.UtcNow }));
            files.Source = new(WorkflowFileArtifactContract.Name, 1, producer, id, "run", sourcePath, "image/png", files.Png.Length, Convert.ToHexString(SHA256.HashData(files.Png)));
            files.Handler = new(new ApplyArtEffectsFileUseCase(new WorkflowArtifactReader(), codec,
                new ImageArtEffectPipeline(new GaussianBlurArtEffectProcessor(), new BloomArtEffectProcessor(), new GrainArtEffectProcessor())), new ExclusivePngCommitter());
            return files;
        }
        public JsonElement Arguments(string stem = "result") => JsonSerializer.SerializeToElement(new
        {
            source = ApplyArtEffectsFileWorkflowActionHandler.ToJson(Source),
            blur = new { enabled = true, sigma = 1 },
            bloom = new { enabled = true, threshold = .72, sigma = 5, strength = .8 },
            grain = new { enabled = true, amount = 3, seed = 0L },
            outputDirectory = OutputRoot,
            fileStem = stem
        });
        public void Dispose()
        {
            if (Directory.Exists(SourceRoot)) Directory.Delete(SourceRoot, true);
            if (Directory.Exists(OutputRoot)) Directory.Delete(OutputRoot, true);
        }
    }
}
