using Microsoft.Extensions.DependencyInjection;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Application.Watermarking;
using ImageLabPlugin.Domain.Frequency;
using ImageLabPlugin.Infrastructure.Cryptography;
using ImageLabPlugin.Infrastructure.ErrorCorrection;
using ImageLabPlugin.Infrastructure.Imaging;
using ImageLabPlugin.Infrastructure.Persistence;
using ImageLabPlugin.Infrastructure.Ui;
using ImageLabPlugin.Infrastructure.Watermarking;

namespace ImageLabPlugin.Plugin;

public static class ImageLabPluginServices
{
    /// <summary>登记插件自己的业务服务；Standalone 可以复用同一个组合入口。</summary>
    public static IServiceCollection AddImageLabPluginServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<Dct8x8Transform>();
        services.AddSingleton<FrequencySpectrumProjector>();
        services.AddSingleton<ReedSolomonCodec>();
        services.AddSingleton<IRandomSource, CryptographicRandomSource>();
        services.AddSingleton<IImageCodec, AvaloniaImageCodec>();
        services.AddSingleton<IAtomicFileWriter, AtomicFileWriter>();
        services.AddSingleton<IImageLabFileDialog, AvaloniaImageLabFileDialog>();
        services.AddSingleton<WatermarkFrameProtocol>();
        services.AddSingleton<FrequencyWatermarkCarrier>();
        services.AddSingleton<IEstimateWatermarkCapacityUseCase, EstimateWatermarkCapacityUseCase>();
        services.AddSingleton<IInspectWatermarkUseCase, InspectWatermarkUseCase>();
        services.AddSingleton<IExtractWatermarkUseCase, ExtractWatermarkUseCase>();
        services.AddSingleton<IEmbedWatermarkUseCase, EmbedWatermarkUseCase>();
        return services;
    }
}
