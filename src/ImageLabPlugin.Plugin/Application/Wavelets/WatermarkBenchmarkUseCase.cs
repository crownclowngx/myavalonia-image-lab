using ImageLabPlugin.Domain.Shared.Analysis;
using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Domain.Robustness;
using ImageLabPlugin.Domain.Shared.Perturbations;

namespace ImageLabPlugin.Application.Wavelets;

/// <summary>在共同 Payload 和共同有限扰动定义下编排 DCT/DWT 载体，不实现任一载体数学循环。</summary>
internal sealed class RunWatermarkCarrierBenchmarkUseCase(
    IEnumerable<IWatermarkBenchmarkCarrier> carriers,
    IEnumerable<IImagePerturbationOperator> perturbations,
    FullReferenceQualityAnalyzer qualityAnalyzer) : IRunWatermarkCarrierBenchmarkUseCase
{
    public async Task<WatermarkCarrierBenchmarkReport> ExecuteAsync(PixelImage source, ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        var carrierList = carriers.OrderBy(carrier => carrier.CarrierId, StringComparer.Ordinal).ToArray();
        if (carrierList.Length != 2) throw new InvalidOperationException("公平比较必须恰好登记 DCT 与 DWT 两个载体适配器。");
        var capacities = carrierList.Select(carrier => carrier.Estimate(source, payload.Length)).ToArray();
        if (capacities.Any(capacity => payload.Length > capacity.MaximumPayloadBytes))
            throw new InvalidOperationException("共同 Payload 超过至少一个载体容量，不能执行伪公平比较。");
        var operatorMap = perturbations.ToDictionary(value => value.Kind);
        var definitions = CreateCases();
        var results = new List<WatermarkBenchmarkCase>();
        foreach (var carrier in carrierList)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var embedded = await carrier.EmbedAndReadAsync(source, payload, cancellationToken).ConfigureAwait(false);
            results.Add(new("none", carrier.CarrierId, embedded.IntegrityValid, embedded.Confidence, embedded.RawBitErrorRate,
                qualityAnalyzer.Analyze(source, embedded.Image, cancellationToken)));
            foreach (var definition in definitions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var current = embedded.Image;
                foreach (var step in definition.Steps)
                {
                    current = await operatorMap[step.Kind].ApplyAsync(current, step.Parameters,
                        PerturbationSeedDeriver.FromCanonicalFacts(
                            0x574156454c4554ul,
                            0,
                            definition.Sequence,
                            0,
                            step.StepId,
                            step.Kind), cancellationToken).ConfigureAwait(false);
                }
                var read = await carrier.ReadAsync(current, embedded, payload, cancellationToken).ConfigureAwait(false);
                results.Add(new(definition.Id, carrier.CarrierId, read.IntegrityValid, read.Confidence, read.RawBitErrorRate,
                    qualityAnalyzer.Analyze(source, embedded.Image, cancellationToken)));
            }
        }
        return new("wavelet-watermark-benchmark-v1", payload.Length, capacities, results,
            ["强度量纲不同：DCT 使用单系数 QIM，DWT 使用系数对差分 QIM。",
             "结论只适用于本图、本 Payload、当前参数和列出的确定性扰动，不代表某种变换普遍更优。",
             "缩放案例使用 0.75 后 4/3 两步恢复；离散舍入可能使边缘尺寸相差 1 像素。"], DateTimeOffset.UtcNow);
    }

    private static IReadOnlyList<BenchmarkDefinition> CreateCases() =>
    [
        new(1, "jpeg-90", [new("jpeg90", PerturbationKind.JpegReencode, true, new JpegParameters(90))]),
        new(2, "jpeg-75", [new("jpeg75", PerturbationKind.JpegReencode, true, new JpegParameters(75))]),
        new(3, "jpeg-50", [new("jpeg50", PerturbationKind.JpegReencode, true, new JpegParameters(50))]),
        new(4, "scale-roundtrip", [new("scale-down", PerturbationKind.Scale, true, new ScaleParameters(0.75m, 0.75m)),
            new("scale-up", PerturbationKind.Scale, true, new ScaleParameters(1.3333333333333333333333333333m, 1.3333333333333333333333333333m))]),
        new(5, "gaussian-noise", [new("noise", PerturbationKind.GaussianNoise, true, new GaussianNoiseParameters(2m))]),
        new(6, "gaussian-blur", [new("blur", PerturbationKind.GaussianBlur, true, new GaussianBlurParameters(0.8m))]),
        new(7, "brightness", [new("brightness", PerturbationKind.Brightness, true, new BrightnessParameters(4))]),
        new(8, "contrast", [new("contrast", PerturbationKind.Contrast, true, new ContrastParameters(1.03m))])
    ];

    private sealed record BenchmarkDefinition(int Sequence, string Id, IReadOnlyList<PerturbationStep> Steps);
}
