using ImageLabPlugin.Application.Fingerprinting;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.Fingerprinting;
using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Infrastructure.Fingerprinting;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class FingerprintUseCaseAndReportTests
{
    [Fact]
    public async Task 用例顺序解码不同尺寸并生成固定三算法摘要()
    {
        var codec = new MapCodec(new Dictionary<string, PixelImage>
        {
            ["D:/secret/a.png"] = FingerprintNormalizationTests.GrayImage(2, 2, [0, 50, 100, 150]),
            ["D:/secret/b.jpg"] = FingerprintNormalizationTests.GrayImage(3, 1, [0, 80, 160])
        });
        var algorithms = FingerprintNormalizationTests.CreateAlgorithms();
        var useCase = new PrepareFingerprintComparisonUseCase(codec, new(), algorithms, new(), new());
        using var session = await useCase.ExecuteAsync(new("D:/secret/a.png", "D:/secret/b.jpg"), default);
        Assert.Equal(["D:/secret/a.png", "D:/secret/b.jpg"], codec.DecodedPaths);
        Assert.Equal(3, session.Summary.Algorithms.Count);
        Assert.Equal(new ImageSize(2, 2), session.Summary.Reference.Size);
        Assert.Equal(new ImageSize(3, 1), session.Summary.Candidate.Size);
        Assert.Equal(FingerprintLumaNormalizer.NormalizationId, session.Summary.NormalizationId);
    }

    [Fact]
    public void Session释放后切断大图并拒绝访问()
    {
        var image = FingerprintNormalizationTests.GrayImage(1, 1, [0]);
        var summary = Summary(image, image, []);
        var session = new FingerprintComparisonSession(image, image.Clone(), image.Clone(), image.Clone(), summary);
        session.Dispose();
        Assert.True(session.IsDisposed);
        Assert.Equal(new ImageSize(1, 1), session.ReferenceImage.Size);
        Assert.Throws<ObjectDisposedException>(session.ThrowIfDisposed);
    }

    [Fact]
    public void 报告只保留文件名算法版本与免责声明()
    {
        var image = FingerprintNormalizationTests.GrayImage(1, 1, [42]);
        var left = new ImageFingerprint(FingerprintAlgorithmId.AverageHash, ulong.MaxValue);
        var result = new FingerprintAlgorithmResult(FingerprintAlgorithmId.AverageHash, left, left, new(0), 8, FingerprintDecision.ExactFingerprintMatch, TimeSpan.FromMilliseconds(1), "限制");
        var serializer = new FingerprintReportSerializer();
        var json = serializer.Serialize(new(1, Summary(image, image, [result])));
        Assert.Contains("ahash-8x8-mean64-luma-v1", json, StringComparison.Ordinal);
        Assert.Contains("fingerprint-reference-policy-v1", json, StringComparison.Ordinal);
        Assert.Contains("a.png", json, StringComparison.Ordinal);
        Assert.DoesNotContain("D:/secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rgba", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("probability", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void 配方排序去重限制二十一点并验证范围()
    {
        var recipe = new FingerprintStabilityRecipe(FingerprintStabilityKind.Jpeg, [80, 100, 80, 40]);
        Assert.Equal([40m, 80m, 100m], recipe.Values);
        Assert.Throws<ArgumentException>(() => new FingerprintStabilityRecipe(FingerprintStabilityKind.Scale, Enumerable.Range(1, 22).Select(value => value / 100m).ToArray()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FingerprintStabilityRecipe(FingerprintStabilityKind.CenterCrop, [11m]));
    }

    private static FingerprintComparisonSummary Summary(PixelImage reference, PixelImage candidate, IReadOnlyList<FingerprintAlgorithmResult> algorithms) => new(
        FingerprintLumaNormalizer.NormalizationId, FingerprintDecisionPolicy.PolicyId,
        new("D:/secret/a.png", reference.Size, false), new("D:/secret/b.png", candidate.Size, false), algorithms,
        algorithms.Count == 0 ? FingerprintOverview.Incomplete : FingerprintOverview.ConsistentlyNear,
        DateTimeOffset.UnixEpoch, "位相似度不是来源概率");

    private sealed class MapCodec(IReadOnlyDictionary<string, PixelImage> images) : IImageCodec
    {
        public List<string> DecodedPaths { get; } = [];
        public Task<PixelImage> DecodeAsync(string path, CancellationToken cancellationToken) { DecodedPaths.Add(path); return Task.FromResult(images[path].Clone()); }
        public Task<PixelImage> DecodeAsync(ReadOnlyMemory<byte> encodedImage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<byte[]> EncodeAsync(PixelImage image, ImageOutputFormat format, int jpegQuality, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
