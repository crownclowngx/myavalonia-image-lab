using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Domain.Robustness;
using ImageLabPlugin.Domain.Shared.Perturbations;

namespace ImageLabPlugin.Infrastructure.Perturbations;

/// <summary>通过正式图片编解码端口实现内存 JPEG 信道；不创建中间文件，也不承担格式转换产品职责。</summary>
internal sealed class JpegReencodeOperator(IImageCodec codec) : IImagePerturbationOperator
{
    public PerturbationKind Kind => PerturbationKind.JpegReencode;
    public async ValueTask<PixelImage> ApplyAsync(PixelImage source, PerturbationParameters parameters, PerturbationExecutionContext trial, CancellationToken cancellationToken)
    {
        if (parameters is not JpegParameters jpeg) throw new ArgumentException("JPEG 参数类型无效。", nameof(parameters));
        PerturbationParameterEditor.Validate(new PerturbationStep(trial.StepId, Kind, true, jpeg));
        var rgba = source.Rgba.Span;
        for (var offset = 3; offset < rgba.Length; offset += 4)
            if (rgba[offset] != 255) throw new InvalidOperationException("JPEG 不支持 Alpha；V1 只允许完全不透明图片进入 JPEG 扰动步骤。");
        var encoded = await codec.EncodeAsync(source, ImageOutputFormat.Jpeg, jpeg.Quality, cancellationToken).ConfigureAwait(false);
        return await codec.DecodeAsync(encoded, cancellationToken).ConfigureAwait(false);
    }
}
