using Microsoft.Extensions.DependencyInjection;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Application.Watermarking;
using ImageLabPlugin.Application.SpectrumAnalysis;
using ImageLabPlugin.Domain.Frequency;
using ImageLabPlugin.Domain.Imaging;
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
        services.AddSingleton<ImageChannelConverter>();
        services.AddSingleton<ImageAnalysisProxyProjector>();
        services.AddSingleton<Fft1DTransform>();
        services.AddSingleton<Fft2DTransform>();
        services.AddSingleton<SpectrumProjector>();
        services.AddSingleton<DctSpectrumProjector>();
        services.AddSingleton<DctBlockAnalyzer>();
        services.AddSingleton<RadialEnergyAnalyzer>();
        services.AddSingleton<FrequencyBandMaskFactory>();
        services.AddSingleton<ReedSolomonCodec>();
        services.AddSingleton<IRandomSource, CryptographicRandomSource>();
        services.AddSingleton<IImageCodec, AvaloniaImageCodec>();
        services.AddSingleton<IAtomicFileWriter, AtomicFileWriter>();
        services.AddSingleton<AvaloniaImageLabFileDialog>();
        services.AddSingleton<IImageFileDialog>(static provider => provider.GetRequiredService<AvaloniaImageLabFileDialog>());
        services.AddSingleton<IPayloadFileDialog>(static provider => provider.GetRequiredService<AvaloniaImageLabFileDialog>());
        services.AddSingleton<WatermarkFrameProtocol>();
        services.AddSingleton<FrequencyWatermarkCarrier>();
        services.AddSingleton<IEstimateWatermarkCapacityUseCase, EstimateWatermarkCapacityUseCase>();
        services.AddSingleton<IInspectWatermarkUseCase, InspectWatermarkUseCase>();
        services.AddSingleton<IExtractWatermarkUseCase, ExtractWatermarkUseCase>();
        services.AddSingleton<IEmbedWatermarkUseCase, EmbedWatermarkUseCase>();
        services.AddSingleton<IAnalyzeSpectrumUseCase, AnalyzeSpectrumUseCase>();
        services.AddSingleton<IInspectDctBlockUseCase, InspectDctBlockUseCase>();
        services.AddSingleton<IReconstructSpectrumBandUseCase, ReconstructSpectrumBandUseCase>();
        services.AddSingleton<IProjectSpectrumUseCase, ProjectSpectrumUseCase>();
        return services;
    }
}
