using ImageLabPlugin.Application.Fingerprinting;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.Imaging;
using ImageLabPlugin.Domain.Robustness;
using ImageLabPlugin.Domain.Robustness.Operators;
using ImageLabPlugin.Domain.Watermarking;

namespace ImageLabPlugin.Infrastructure.Fingerprinting;

/// <summary>把四种指纹稳定性操作适配到既有正式编解码和扰动 Strategy，避免复制扰动公式。</summary>
internal sealed class FingerprintStabilityChannel(
    IImageCodec codec,
    IEnumerable<IImagePerturbationOperator> operators) : IFingerprintStabilityChannel
{
    private readonly IReadOnlyDictionary<PerturbationKind, IImagePerturbationOperator> _operators = operators
        .Where(value => value.Kind is PerturbationKind.Scale or PerturbationKind.Brightness or PerturbationKind.Crop)
        .ToDictionary(value => value.Kind);

    public async ValueTask<FingerprintStabilitySample> ApplyAsync(PixelImage source, FingerprintStabilityKind kind, decimal value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (kind == FingerprintStabilityKind.Jpeg)
        {
            var rgba = source.Rgba.Span;
            for (var offset = 3; offset < rgba.Length; offset += 4)
                if (rgba[offset] != 255) throw new InvalidOperationException("JPEG 稳定性试验不适用于含透明像素的图片。");
            var bytes = await codec.EncodeAsync(source, ImageOutputFormat.Jpeg, decimal.ToInt32(value), cancellationToken).ConfigureAwait(false);
            var decoded = await codec.DecodeAsync(bytes, cancellationToken).ConfigureAwait(false);
            return new(decoded, bytes.LongLength);
        }

        var (operatorKind, parameters) = CreateParameters(source.Size, kind, value);
        var key = new RobustnessCaseKey(EmbeddingProfileId.Balanced, 0, value, 0);
        var context = new DeterministicTrialContext(0UL, key, $"fingerprint-{kind}", operatorKind);
        var image = await _operators[operatorKind].ApplyAsync(source, parameters, context, cancellationToken).ConfigureAwait(false);
        return new(image, null);
    }

    private static (PerturbationKind Kind, PerturbationParameters Parameters) CreateParameters(ImageSize size, FingerprintStabilityKind kind, decimal value) => kind switch
    {
        FingerprintStabilityKind.Scale => (PerturbationKind.Scale, new ScaleParameters(value, value)),
        FingerprintStabilityKind.Brightness => (PerturbationKind.Brightness, new BrightnessParameters(decimal.ToInt32(decimal.Round(255m * value / 100m)))),
        FingerprintStabilityKind.CenterCrop => (PerturbationKind.Crop, CreateCrop(size, value)),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "不支持的稳定性试验类型。")
    };

    private static CropParameters CreateCrop(ImageSize size, decimal percent)
    {
        var horizontal = Math.Min((size.Width - 1) / 2, decimal.ToInt32(decimal.Round(size.Width * percent / 100m)));
        var vertical = Math.Min((size.Height - 1) / 2, decimal.ToInt32(decimal.Round(size.Height * percent / 100m)));
        return new CropParameters(horizontal, vertical, horizontal, vertical);
    }
}
