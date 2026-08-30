using Microsoft.Extensions.DependencyInjection;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Application.Watermarking;
using ImageLabPlugin.Application.SpectrumAnalysis;
using ImageLabPlugin.Application.ImageComparison;
using ImageLabPlugin.Domain.Comparison;
using ImageLabPlugin.Domain.Frequency;
using ImageLabPlugin.Domain.Imaging;
using ImageLabPlugin.Infrastructure.Cryptography;
using ImageLabPlugin.Infrastructure.ErrorCorrection;
using ImageLabPlugin.Infrastructure.Imaging;
using ImageLabPlugin.Infrastructure.Persistence;
using ImageLabPlugin.Infrastructure.Ui;
using ImageLabPlugin.Infrastructure.Watermarking;
using ImageLabPlugin.Application.Robustness;
using ImageLabPlugin.Domain.Robustness;
using ImageLabPlugin.Domain.Robustness.Operators;
using ImageLabPlugin.Infrastructure.Robustness;
using ImageLabPlugin.Application.Fingerprinting;
using ImageLabPlugin.Domain.Fingerprinting;
using ImageLabPlugin.Infrastructure.Fingerprinting;
using ImageLabPlugin.Application.BitPlanes;
using ImageLabPlugin.Domain.BitPlanes;
using ImageLabPlugin.Application.LsbSteganography;
using ImageLabPlugin.Domain.Steganography;
using ImageLabPlugin.Infrastructure.Steganography;
using ImageLabPlugin.Application.Convolution;
using ImageLabPlugin.Domain.Convolution;
using ImageLabPlugin.Application.Wavelets;
using ImageLabPlugin.Domain.Wavelets;
using ImageLabPlugin.Infrastructure.Wavelets;
using ImageLabPlugin.Application.FrequencyFiltering;
using ImageLabPlugin.Domain.FrequencyFiltering;

namespace ImageLabPlugin.Plugin;

public static class ImageLabPluginServices
{
    /// <summary>登记插件自己的业务服务；Standalone 可以复用同一个组合入口。</summary>
    public static IServiceCollection AddImageLabPluginServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        AddSharedDomainServices(services);
        AddInfrastructurePorts(services);
        AddWatermarkServices(services);
        AddSpectrumServices(services);
        AddImageComparisonServices(services);
        AddRobustnessServices(services);
        AddFingerprintServices(services);
        AddBitPlaneServices(services);
        AddLsbSteganographyServices(services);
        AddConvolutionServices(services);
        AddWaveletServices(services);
        AddFrequencyFilterServices(services);
        return services;
    }

    /// <summary>登记被多个产品领域复用的图像、DCT 与 FFT 数值基础。</summary>
    private static void AddSharedDomainServices(IServiceCollection services)
    {
        services.AddSingleton<Dct8x8Transform>();
        services.AddSingleton<LowFrequencyDctTransform>();
        services.AddSingleton<ImageChannelConverter>();
        services.AddSingleton<ImageAnalysisProxyProjector>();
        services.AddSingleton<Fft1DTransform>();
        services.AddSingleton<Fft2DTransform>();
    }

    /// <summary>登记 Avalonia、文件系统和安全随机源等领域外端口。</summary>
    private static void AddInfrastructurePorts(IServiceCollection services)
    {
        services.AddSingleton<IRandomSource, CryptographicRandomSource>();
        services.AddSingleton<IImageCodec, AvaloniaImageCodec>();
        services.AddSingleton<IAtomicFileWriter, AtomicFileWriter>();
        services.AddSingleton<AvaloniaImageLabFileDialog>();
        services.AddSingleton<IImageFileDialog>(static provider => provider.GetRequiredService<AvaloniaImageLabFileDialog>());
        services.AddSingleton<IPayloadFileDialog>(static provider => provider.GetRequiredService<AvaloniaImageLabFileDialog>());
        services.AddSingleton<IComparisonReportFileDialog>(static provider => provider.GetRequiredService<AvaloniaImageLabFileDialog>());
        services.AddSingleton<IRobustnessReportFileDialog>(static provider => provider.GetRequiredService<AvaloniaImageLabFileDialog>());
        services.AddSingleton<IFingerprintReportFileDialog>(static provider => provider.GetRequiredService<AvaloniaImageLabFileDialog>());
        services.AddSingleton<ILsbReportFileDialog>(static provider => provider.GetRequiredService<AvaloniaImageLabFileDialog>());
        services.AddSingleton<IWaveletReportFileDialog>(static provider => provider.GetRequiredService<AvaloniaImageLabFileDialog>());
        services.AddSingleton<ITextClipboard>(static provider => provider.GetRequiredService<AvaloniaImageLabFileDialog>());
    }

    /// <summary>登记频域隐式水印协议、载体和四个应用用例。</summary>
    private static void AddWatermarkServices(IServiceCollection services)
    {
        services.AddSingleton<ReedSolomonCodec>();
        services.AddSingleton<WatermarkFrameProtocol>();
        services.AddSingleton<FrequencyWatermarkCarrier>();
        services.AddSingleton<IEstimateWatermarkCapacityUseCase, EstimateWatermarkCapacityUseCase>();
        services.AddSingleton<IInspectWatermarkUseCase, InspectWatermarkUseCase>();
        services.AddSingleton<IExtractWatermarkUseCase, ExtractWatermarkUseCase>();
        services.AddSingleton<IEmbedWatermarkUseCase, EmbedWatermarkUseCase>();
    }

    /// <summary>登记全局 FFT、分块 DCT、频带分析和频谱投影用例。</summary>
    private static void AddSpectrumServices(IServiceCollection services)
    {
        services.AddSingleton<FrequencySpectrumProjector>();
        services.AddSingleton<SpectrumProjector>();
        services.AddSingleton<DctSpectrumProjector>();
        services.AddSingleton<DctBlockAnalyzer>();
        services.AddSingleton<RadialEnergyAnalyzer>();
        services.AddSingleton<FrequencyBandMaskFactory>();
        services.AddSingleton<IAnalyzeSpectrumUseCase, AnalyzeSpectrumUseCase>();
        services.AddSingleton<IInspectDctBlockUseCase, InspectDctBlockUseCase>();
        services.AddSingleton<IReconstructSpectrumBandUseCase, ReconstructSpectrumBandUseCase>();
        services.AddSingleton<IProjectSpectrumUseCase, ProjectSpectrumUseCase>();
    }

    /// <summary>登记同尺寸图像比较、差异投影、指标和摘要导出。</summary>
    private static void AddImageComparisonServices(IServiceCollection services)
    {
        services.AddSingleton<ImagePairValidator>();
        services.AddSingleton<FullReferenceQualityAnalyzer>();
        services.AddSingleton<ImageHistogramAnalyzer>();
        services.AddSingleton<ImageDifferenceProxyAnalyzer>();
        services.AddSingleton<ImageDifferenceProxyProjector>();
        services.AddSingleton<DifferenceHeatmapProjector>();
        services.AddSingleton<ImagePairPixelInspector>();
        services.AddSingleton<ImageComparisonSummarySerializer>();
        services.AddSingleton<IPrepareImageComparisonUseCase, PrepareImageComparisonUseCase>();
        services.AddSingleton<IProjectImageDifferenceUseCase, ProjectImageDifferenceUseCase>();
        services.AddSingleton<IInspectImagePairUseCase, InspectImagePairUseCase>();
        services.AddSingleton<IExportComparisonSummaryUseCase, ExportComparisonSummaryUseCase>();
    }

    /// <summary>登记受控扰动 Strategy、实验协调、诊断与报告用例。</summary>
    private static void AddRobustnessServices(IServiceCollection services)
    {
        services.AddSingleton<RobustnessRecipeValidator>();
        services.AddSingleton<RobustnessExperimentPlanner>();
        services.AddSingleton<IImagePerturbationOperator, DeterministicPixelOperator>();
        services.AddSingleton<IImagePerturbationOperator, GaussianNoiseOperator>();
        services.AddSingleton<IImagePerturbationOperator, SaltPepperNoiseOperator>();
        services.AddSingleton<IImagePerturbationOperator, BrightnessOperator>();
        services.AddSingleton<IImagePerturbationOperator, ContrastOperator>();
        services.AddSingleton<IImagePerturbationOperator, GammaOperator>();
        services.AddSingleton<IImagePerturbationOperator, SaturationOperator>();
        services.AddSingleton<IImagePerturbationOperator, ColorBiasOperator>();
        services.AddSingleton<IImagePerturbationOperator, GaussianBlurOperator>();
        services.AddSingleton<IImagePerturbationOperator, MedianBlurOperator>();
        services.AddSingleton<IImagePerturbationOperator, UnsharpMaskOperator>();
        services.AddSingleton<IImagePerturbationOperator, ScaleOperator>();
        services.AddSingleton<IImagePerturbationOperator, CropOperator>();
        services.AddSingleton<IImagePerturbationOperator, PadOperator>();
        services.AddSingleton<IImagePerturbationOperator, TranslateOperator>();
        services.AddSingleton<IImagePerturbationOperator, RotateOperator>();
        services.AddSingleton<IImagePerturbationOperator, PerspectiveOperator>();
        services.AddSingleton<IImagePerturbationOperator, JpegReencodeOperator>();
        services.AddSingleton<PerturbationChainExecutor>();
        services.AddSingleton<IWatermarkDiagnosticReader, WatermarkDiagnosticReader>();
        services.AddSingleton<RobustnessReportSerializer>();
        services.AddSingleton<IPrepareRobustnessBaselineUseCase, PrepareRobustnessBaselineUseCase>();
        services.AddSingleton<IPlanRobustnessExperimentUseCase, PlanRobustnessExperimentUseCase>();
        services.AddSingleton<IRunRobustnessExperimentUseCase, RunRobustnessExperimentUseCase>();
        services.AddSingleton<IExportRobustnessReportUseCase, RobustnessReportExportUseCase>();
    }

    /// <summary>登记 aHash、dHash、pHash 及指纹比较与稳定性用例。</summary>
    private static void AddFingerprintServices(IServiceCollection services)
    {
        services.AddSingleton<FingerprintLumaNormalizer>();
        services.AddSingleton<FingerprintDistanceCalculator>();
        services.AddSingleton<FingerprintDecisionPolicy>();
        services.AddSingleton<IImageFingerprintAlgorithm, AverageHashAlgorithm>();
        services.AddSingleton<IImageFingerprintAlgorithm, DifferenceHashAlgorithm>();
        services.AddSingleton<IImageFingerprintAlgorithm, PerceptualHashAlgorithm>();
        services.AddSingleton<FingerprintReportSerializer>();
        services.AddSingleton<IPrepareFingerprintComparisonUseCase, PrepareFingerprintComparisonUseCase>();
        services.AddSingleton<IFingerprintStabilityChannel, FingerprintStabilityChannel>();
        services.AddSingleton<IRunFingerprintStabilityUseCase, RunFingerprintStabilityUseCase>();
        services.AddSingleton<IExportFingerprintReportUseCase, FingerprintReportExportUseCase>();
        services.AddSingleton<IFingerprintObservationProbe, FingerprintObservationProbe>();
    }

    /// <summary>登记位平面抽取、统计、投影、探针和 PNG 重建用例。</summary>
    private static void AddBitPlaneServices(IServiceCollection services)
    {
        services.AddSingleton<BitPlaneChannelExtractor>();
        services.AddSingleton<BitPlaneStatisticsAnalyzer>();
        services.AddSingleton<BitPlaneProjector>();
        services.AddSingleton<BitPlaneReconstructor>();
        services.AddSingleton<BitPlanePixelInspector>();
        services.AddSingleton<IPrepareBitPlaneSessionUseCase, PrepareBitPlaneSessionUseCase>();
        services.AddSingleton<IAnalyzeBitPlaneChannelUseCase, AnalyzeBitPlaneChannelUseCase>();
        services.AddSingleton<IProjectBitPlaneViewUseCase, ProjectBitPlaneViewUseCase>();
        services.AddSingleton<IExportBitPlaneImageUseCase, ExportBitPlaneImageUseCase>();
    }

    /// <summary>登记独立 ILSB Frame、槽位策略、统计、脆弱性和导出用例。</summary>
    private static void AddLsbSteganographyServices(IServiceCollection services)
    {
        services.AddSingleton<ILsbPayloadFileReader, LsbPayloadFileReader>();
        services.AddSingleton<LsbFrameCodec>();
        services.AddSingleton<LsbCapacityCalculator>();
        services.AddSingleton<ILsbSlotOrder, SequentialLsbSlotOrder>();
        services.AddSingleton<ILsbSlotOrder, PseudoRandomLsbSlotOrder>();
        services.AddSingleton<LsbEmbeddingEngine>();
        services.AddSingleton<LsbExtractionEngine>();
        services.AddSingleton<LsbStatisticsAnalyzer>();
        services.AddSingleton<LsbPreviewProjector>();
        services.AddSingleton<LsbPixelInspector>();
        services.AddSingleton<LsbExperimentReportSerializer>();
        services.AddSingleton<IPrepareLsbExperimentUseCase, PrepareLsbExperimentUseCase>();
        services.AddSingleton<IEstimateLsbCapacityUseCase, EstimateLsbCapacityUseCase>();
        services.AddSingleton<IEmbedAndAnalyzeLsbUseCase, EmbedAndAnalyzeLsbUseCase>();
        services.AddSingleton<IExtractLsbPayloadUseCase, ExtractLsbPayloadUseCase>();
        services.AddSingleton<IRunLsbFragilityUseCase, RunLsbFragilityUseCase>();
        services.AddSingleton<IExportLsbImageUseCase, ExportLsbImageUseCase>();
        services.AddSingleton<IExportLsbReportUseCase, ExportLsbReportUseCase>();
        services.AddSingleton<ILoadLsbPayloadUseCase, LoadLsbPayloadUseCase>();
        services.AddSingleton<IInspectLsbPixelUseCase, InspectLsbPixelUseCase>();
    }

    /// <summary>登记卷积核、空间执行、频响、解释及代理/完整尺寸用例。</summary>
    private static void AddConvolutionServices(IServiceCollection services)
    {
        // 卷积领域类均为无状态数学服务；Document/Session 才拥有每实例图片、结果和取消状态。
        services.AddSingleton<ConvolutionKernelParser>();
        services.AddSingleton<ConvolutionPresetFactory>();
        services.AddSingleton<SpatialConvolver>();
        services.AddSingleton<GradientCombiner>();
        services.AddSingleton<ConvolutionImageProcessor>();
        services.AddSingleton<KernelFrequencyResponseAnalyzer>();
        services.AddSingleton<ConvolutionDifferenceProjector>();
        services.AddSingleton<ConvolutionPixelInspector>();
        services.AddSingleton<IPrepareConvolutionSessionUseCase, PrepareConvolutionSessionUseCase>();
        services.AddSingleton<IRenderConvolutionPreviewUseCase, RenderConvolutionPreviewUseCase>();
        services.AddSingleton<IInspectConvolutionPixelUseCase, InspectConvolutionPixelUseCase>();
        services.AddSingleton<IRenderKernelResponseUseCase, RenderKernelResponseUseCase>();
        services.AddSingleton<IRenderFullConvolutionUseCase, RenderFullConvolutionUseCase>();
        services.AddSingleton<IExportConvolutionImageUseCase, ExportConvolutionImageUseCase>();
    }

    /// <summary>登记两种小波 Strategy、不可变处理服务、窄用例与 DCT/DWT benchmark Adapter。</summary>
    private static void AddWaveletServices(IServiceCollection services)
    {
        services.AddSingleton<HaarWaveletTransform>();
        services.AddSingleton<Cdf53WaveletTransform>();
        services.AddSingleton<IWaveletTransform>(static provider => provider.GetRequiredService<HaarWaveletTransform>());
        services.AddSingleton<IWaveletTransform>(static provider => provider.GetRequiredService<Cdf53WaveletTransform>());
        services.AddSingleton<WaveletTransformCatalog>();
        services.AddSingleton<WaveletNoiseEstimator>();
        services.AddSingleton<WaveletThresholdProcessor>();
        services.AddSingleton<WaveletSubbandProjector>();
        services.AddSingleton<WaveletImageReconstructor>();
        services.AddSingleton<DwtWatermarkCarrier>();
        services.AddSingleton<IWatermarkBenchmarkCarrier, DctWatermarkBenchmarkAdapter>();
        services.AddSingleton<IWatermarkBenchmarkCarrier, DwtWatermarkBenchmarkAdapter>();
        services.AddSingleton<IWaveletReportSerializer, WaveletExperimentReportSerializer>();
        services.AddSingleton<IPrepareWaveletSessionUseCase, PrepareWaveletSessionUseCase>();
        services.AddSingleton<IDecomposeWaveletUseCase, DecomposeWaveletUseCase>();
        services.AddSingleton<IDenoiseWaveletUseCase, DenoiseWaveletUseCase>();
        services.AddSingleton<IReconstructWaveletLevelUseCase, ReconstructWaveletLevelUseCase>();
        services.AddSingleton<IRunWaveletQualityScanUseCase, RunWaveletQualityScanUseCase>();
        services.AddSingleton<IRunWatermarkCarrierBenchmarkUseCase, RunWatermarkCarrierBenchmarkUseCase>();
        services.AddSingleton<IExportWaveletImageUseCase, ExportWaveletImageUseCase>();
        services.AddSingleton<IExportWaveletReportUseCase, ExportWaveletReportUseCase>();
    }

    /// <summary>登记无状态频域滤波数值服务和五个窄应用用例；每实例状态只存在于 Document/Session。</summary>
    private static void AddFrequencyFilterServices(IServiceCollection services)
    {
        services.AddSingleton<RadialFilterResponse>();
        services.AddSingleton<FrequencySpectrumBuilder>();
        services.AddSingleton<FrequencyFilterMaskFactory>();
        services.AddSingleton<FrequencyFilterEngine>();
        services.AddSingleton<FrequencySignalProjector>();
        services.AddSingleton<FrequencySideEffectAnalyzer>();
        services.AddSingleton<FrequencyDifferenceProjector>();
        services.AddSingleton<FrequencyImpulseResponseFactory>();
        services.AddSingleton<FrequencySpatialComparator>();
        services.AddSingleton<IPrepareFrequencyFilterSessionUseCase, PrepareFrequencyFilterSessionUseCase>();
        services.AddSingleton<ApplyFrequencyFilterUseCase>();
        services.AddSingleton<IApplyFrequencyFilterUseCase>(static provider => provider.GetRequiredService<ApplyFrequencyFilterUseCase>());
        services.AddSingleton<ICompareFrequencySpatialUseCase, CompareFrequencySpatialUseCase>();
        services.AddSingleton<IRenderFullFrequencyFilterUseCase, RenderFullFrequencyFilterUseCase>();
        services.AddSingleton<IExportFrequencyFilterImageUseCase, ExportFrequencyFilterImageUseCase>();
    }
}
