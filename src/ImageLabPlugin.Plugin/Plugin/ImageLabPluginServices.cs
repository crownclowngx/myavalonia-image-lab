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
using ImageLabPlugin.Application.FrequencyMaskEditing;
using ImageLabPlugin.Domain.FrequencyMaskEditing;
using ImageLabPlugin.Application.PeriodicNoiseRemoval;
using ImageLabPlugin.Domain.PeriodicNoiseRemoval;
using ImageLabPlugin.Application.SvdDecomposition;
using ImageLabPlugin.Domain.SvdDecomposition;
using ImageLabPlugin.Application.ColorTransfer;
using ImageLabPlugin.Domain.ColorTransfer;
using ImageLabPlugin.Infrastructure.ColorTransfer;
using ImageLabPlugin.Application.SeamCarving;
using ImageLabPlugin.Domain.SeamCarving;
using ImageLabPlugin.Infrastructure.SeamCarving;
using ImageLabPlugin.Application.PoissonBlending;
using ImageLabPlugin.Domain.PoissonBlending;
using ImageLabPlugin.Infrastructure.PoissonBlending;
using ImageLabPlugin.Application.SpectralArt;
using ImageLabPlugin.Domain.SpectralArt;
using ImageLabPlugin.Application.HybridImage;
using ImageLabPlugin.Domain.HybridImage;
using ImageLabPlugin.Application.MagnitudePhaseSwap;
using ImageLabPlugin.Domain.MagnitudePhaseSwap;
using ImageLabPlugin.Application.ImageOscilloscope;
using ImageLabPlugin.Domain.ImageOscilloscope;

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
        AddFrequencyMaskEditorServices(services);
        AddPeriodicNoiseRemovalServices(services);
        AddSvdDecompositionServices(services);
        AddColorTransferServices(services);
        AddSeamCarvingServices(services);
        AddPoissonBlendingServices(services);
        AddSpectralArtServices(services);
        AddHybridImageServices(services);
        AddMagnitudePhaseServices(services);
        AddImageOscilloscopeServices(services);
        return services;
    }

    /// <summary>登记被多个产品领域复用的图像、DCT 与 FFT 数值基础。</summary>
    private static void AddSharedDomainServices(IServiceCollection services)
    {
        services.AddSingleton<Dct8x8Transform>();
        services.AddSingleton<LowFrequencyDctTransform>();
        services.AddSingleton<ImageChannelConverter>();
        services.AddSingleton<ImageAreaResampler>();
        services.AddSingleton<ImageAnalysisProxyProjector>();
        services.AddSingleton<Fft1DTransform>();
        services.AddSingleton<Fft2DTransform>();
        services.AddSingleton<FrequencyInverseTransformer>();
        services.AddSingleton<FrequencyMaskApplier>();
        services.AddSingleton<FrequencyGainSpectrumProjector>();
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
        services.AddSingleton<ISvdFileDialog>(static provider => provider.GetRequiredService<AvaloniaImageLabFileDialog>());
        services.AddSingleton<IFrequencyMaskRecipeFileDialog>(static provider => provider.GetRequiredService<AvaloniaImageLabFileDialog>());
        services.AddSingleton<IPeriodicNoiseFileDialog>(static provider => provider.GetRequiredService<AvaloniaImageLabFileDialog>());
        services.AddSingleton<IColorTransferFileDialog>(static provider => provider.GetRequiredService<AvaloniaImageLabFileDialog>());
        services.AddSingleton<ISeamCarvingFileDialog>(static provider => provider.GetRequiredService<AvaloniaImageLabFileDialog>());
        services.AddSingleton<IPoissonBlendingFileDialog>(static provider => provider.GetRequiredService<AvaloniaImageLabFileDialog>());
        services.AddSingleton<ISpectralArtFileDialog>(static provider => provider.GetRequiredService<AvaloniaImageLabFileDialog>());
        services.AddSingleton<IHybridImageFileDialog>(static provider => provider.GetRequiredService<AvaloniaImageLabFileDialog>());
        services.AddSingleton<IMagnitudePhaseFileDialog>(static provider => provider.GetRequiredService<AvaloniaImageLabFileDialog>());
        services.AddSingleton<ITextClipboard>(static provider => provider.GetRequiredService<AvaloniaImageLabFileDialog>());
        services.AddSingleton<ITextFileReader, BoundedTextFileReader>();
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

    /// <summary>登记遮罩编辑、严格配方边界与七个窄用例；Document/Session 仍由各自 Scope 独占。</summary>
    private static void AddFrequencyMaskEditorServices(IServiceCollection services)
    {
        services.AddSingleton<ConjugateMaskWriter>();
        services.AddSingleton<FrequencyMaskRasterizer>();
        services.AddSingleton<FrequencyMaskDiagnostics>();
        services.AddSingleton<IFrequencyMaskRecipeSerializer, FrequencyMaskRecipeSerializer>();
        services.AddSingleton<IPrepareFrequencyMaskEditorSessionUseCase, PrepareFrequencyMaskEditorSessionUseCase>();
        services.AddSingleton<RenderFrequencyMaskUseCase>();
        services.AddSingleton<IRenderFrequencyMaskUseCase>(static provider => provider.GetRequiredService<RenderFrequencyMaskUseCase>());
        services.AddSingleton<IRenderFullFrequencyMaskUseCase, RenderFullFrequencyMaskUseCase>();
        services.AddSingleton<IExportFrequencyMaskImageUseCase, ExportFrequencyMaskImageUseCase>();
        services.AddSingleton<IInspectFrequencyMaskPointUseCase, InspectFrequencyMaskPointUseCase>();
        services.AddSingleton<IImportFrequencyMaskRecipeUseCase, ImportFrequencyMaskRecipeUseCase>();
        services.AddSingleton<IExportFrequencyMaskRecipeUseCase, ExportFrequencyMaskRecipeUseCase>();
    }

    /// <summary>登记周期峰检测、共轭陷波、损失诊断和独立导入导出窄用例。</summary>
    private static void AddPeriodicNoiseRemovalServices(IServiceCollection services)
    {
        services.AddSingleton<RadialLogPowerBaseline>();
        services.AddSingleton<PeriodicPeakRiskAssessor>();
        services.AddSingleton<PeriodicPeakDetector>();
        services.AddSingleton<NotchResponse>();
        services.AddSingleton<NotchMaskFactory>();
        services.AddSingleton<PeriodicNoiseLossAnalyzer>();
        services.AddSingleton<IPeriodicNoiseRecipeSerializer, PeriodicNoiseRecipeSerializer>();
        services.AddSingleton<IPeriodicNoiseCandidateSummarySerializer, PeriodicNoiseCandidateSummarySerializer>();
        services.AddSingleton<IPreparePeriodicNoiseSessionUseCase, PreparePeriodicNoiseSessionUseCase>();
        services.AddSingleton<IDetectPeriodicNoiseCandidatesUseCase, DetectPeriodicNoiseCandidatesUseCase>();
        services.AddSingleton<IMapPeriodicSpectrumSelectionUseCase, MapPeriodicSpectrumSelectionUseCase>();
        services.AddSingleton<RenderPeriodicNoisePreviewUseCase>();
        services.AddSingleton<IRenderPeriodicNoisePreviewUseCase>(static provider =>
            provider.GetRequiredService<RenderPeriodicNoisePreviewUseCase>());
        services.AddSingleton<IRenderFullPeriodicNoiseResultUseCase, RenderFullPeriodicNoiseResultUseCase>();
        services.AddSingleton<IImportPeriodicNoiseRecipeUseCase, ImportPeriodicNoiseRecipeUseCase>();
        services.AddSingleton<IExportPeriodicNoiseRecipeUseCase, ExportPeriodicNoiseRecipeUseCase>();
        services.AddSingleton<IExportPeriodicNoiseCandidateSummaryUseCase, ExportPeriodicNoiseCandidateSummaryUseCase>();
        services.AddSingleton<IExportPeriodicNoiseArtifactUseCase, ExportPeriodicNoiseArtifactUseCase>();
    }

    /// <summary>登记无状态 SVD 数值服务和七个窄应用用例；因子缓存只存在于每个 SvdSession。</summary>
    private static void AddSvdDecompositionServices(IServiceCollection services)
    {
        services.AddSingleton<JacobiSvdDecomposer>();
        services.AddSingleton<SingularValueEnergyAnalyzer>();
        services.AddSingleton<LowRankReconstructor>();
        services.AddSingleton<SvdComponentProjector>();
        services.AddSingleton<SvdColorStrategyExecutor>();
        services.AddSingleton<SvdImageReconstructor>();
        services.AddSingleton<SvdReconstructionAnalyzer>();
        services.AddSingleton<ISvdReportSerializer, SvdReportSerializer>();
        services.AddSingleton<IPrepareSvdSessionUseCase, PrepareSvdSessionUseCase>();
        services.AddSingleton<IDecomposeSvdUseCase, DecomposeSvdUseCase>();
        services.AddSingleton<IReconstructSvdRankUseCase, ReconstructSvdRankUseCase>();
        services.AddSingleton<IProjectSvdComponentUseCase, ProjectSvdComponentUseCase>();
        services.AddSingleton<ICompareSvdStrategiesUseCase, CompareSvdStrategiesUseCase>();
        services.AddSingleton<IExportSvdImageUseCase, ExportSvdImageUseCase>();
        services.AddSingleton<IExportSvdReportUseCase, ExportSvdReportUseCase>();
    }

    /// <summary>登记颜色数学、窄用例和每 Document Scope 独占的 Session。</summary>
    private static void AddColorTransferServices(IServiceCollection services)
    {
        // 颜色服务均无状态；只有 Session 保存目标、参考、冻结调色板和当前结果。
        services.AddSingleton<SrgbColorSpace>();
        services.AddSingleton<CieLabColorSpace>();
        services.AddSingleton<HsvColorSpace>();
        services.AddSingleton<CieDeltaE>();
        services.AddSingleton<SrgbGamutMapper>();
        services.AddSingleton<ColorDistributionAnalyzer>();
        services.AddSingleton<RgbColorAggregator>();
        services.AddSingleton<DominantColorClusterer>();
        services.AddSingleton<PaletteSorter>();
        services.AddSingleton<PerceptualDifferenceAnalyzer>();
        services.AddSingleton<ColorPixelInspector>();
        services.AddSingleton<LabStatisticsTransfer>();
        services.AddSingleton<FixedPaletteRemapper>();
        services.AddSingleton<IColorTransferReportSerializer, ColorTransferReportSerializer>();
        services.AddSingleton<IPrepareColorTransferSessionUseCase, PrepareColorTransferSessionUseCase>();
        services.AddSingleton<IAnalyzeColorDistributionsUseCase, AnalyzeColorDistributionsUseCase>();
        services.AddSingleton<IFreezePaletteUseCase, FreezePaletteUseCase>();
        services.AddSingleton<IRunColorTransferUseCase, RunColorTransferUseCase>();
        services.AddSingleton<IRemapToPaletteUseCase, RemapToPaletteUseCase>();
        services.AddSingleton<IExportColorResultUseCase, ExportColorResultUseCase>();
        services.AddSingleton<IExportColorReportUseCase, ExportColorReportUseCase>();
        services.AddScoped<ColorTransferSession>();
    }

    /// <summary>登记 Seam Carving 无状态数值服务、两个参考缩放 Strategy、窄用例和 scoped Session。</summary>
    private static void AddSeamCarvingServices(IServiceCollection services)
    {
        // Sobel、DP、路径变形和预算服务没有实例状态，可安全跨 Document 复用；图片与计划只在 Session 中。
        services.AddSingleton<SeamLumaProjector>();
        services.AddSingleton<SobelEnergyCalculator>();
        services.AddSingleton<SeamEnergyPreviewProjector>();
        services.AddSingleton<SeamMaskPreviewProjector>();
        services.AddSingleton<MinimumEnergySeamFinder>();
        services.AddSingleton<SeamRemover>();
        services.AddSingleton<SeamInsertionPlanner>();
        services.AddSingleton<SeamInserter>();
        services.AddSingleton<SeamMaskRasterizer>();
        services.AddSingleton<SeamResourceEstimator>();
        services.AddSingleton<SeamResizePlanner>();
        services.AddSingleton<BilinearReferenceResampler>();
        services.AddSingleton<BicubicReferenceResampler>();
        services.AddSingleton<IReferenceImageResampler>(static provider => provider.GetRequiredService<BilinearReferenceResampler>());
        services.AddSingleton<IReferenceImageResampler>(static provider => provider.GetRequiredService<BicubicReferenceResampler>());
        services.AddSingleton<ISeamCarvingReportSerializer, SeamCarvingReportSerializer>();
        services.AddSingleton<IPrepareSeamCarvingSessionUseCase, PrepareSeamCarvingSessionUseCase>();
        services.AddSingleton<IEditSeamMaskUseCase, EditSeamMaskUseCase>();
        services.AddSingleton<IPlanSeamResizeUseCase, PlanSeamResizeUseCase>();
        services.AddSingleton<IPreviewNextSeamUseCase, PreviewNextSeamUseCase>();
        services.AddSingleton<IApplySeamStepUseCase, ApplySeamStepUseCase>();
        services.AddSingleton<IRunSeamPlaybackUseCase, RunSeamPlaybackUseCase>();
        services.AddSingleton<ICompareSeamResizeUseCase, CompareSeamResizeUseCase>();
        services.AddSingleton<IExportSeamResultUseCase, ExportSeamResultUseCase>();
        services.AddSingleton<IExportSeamReportUseCase, ExportSeamReportUseCase>();
        services.AddScoped<SeamCarvingSession>();
    }

    /// <summary>登记 Poisson 无状态数学服务、唯一 guidance Strategy 变化点、窄用例和 scoped Session。</summary>
    private static void AddPoissonBlendingServices(IServiceCollection services)
    {
        // 图片、遮罩、问题和解只属于 PoissonBlendingSession；singleton 服务不保存任何 Document 状态。
        services.AddSingleton<NormalCloneGuidanceStrategy>();
        services.AddSingleton<MixedGradientGuidanceStrategy>();
        services.AddSingleton<MonochromeGuidanceStrategy>();
        services.AddSingleton<IPoissonGuidanceStrategy>(static provider => provider.GetRequiredService<NormalCloneGuidanceStrategy>());
        services.AddSingleton<IPoissonGuidanceStrategy>(static provider => provider.GetRequiredService<MixedGradientGuidanceStrategy>());
        services.AddSingleton<IPoissonGuidanceStrategy>(static provider => provider.GetRequiredService<MonochromeGuidanceStrategy>());
        services.AddSingleton<PoissonGuidanceCatalog>();
        services.AddSingleton<PoissonMaskRasterizer>();
        services.AddSingleton<PoissonMaskTopologyAnalyzer>();
        services.AddSingleton<PoissonPlacementValidator>();
        services.AddSingleton<PoissonResourceEstimator>();
        services.AddSingleton<PoissonProblemBuilder>();
        services.AddSingleton<PoissonRelaxationSolver>();
        services.AddSingleton<PoissonBlendComposer>();
        services.AddSingleton<DirectAlphaCompositor>();
        services.AddSingleton<PoissonResidualProjector>();
        services.AddSingleton<PoissonFieldProjector>();
        services.AddSingleton<PoissonBlendDiagnosticsAnalyzer>();
        services.AddSingleton<IPoissonBlendingReportSerializer, PoissonBlendingReportSerializer>();
        services.AddSingleton<IPreparePoissonSessionUseCase, PreparePoissonSessionUseCase>();
        services.AddSingleton<IEditPoissonMaskUseCase, EditPoissonMaskUseCase>();
        services.AddSingleton<IPlacePoissonRegionUseCase, PlacePoissonRegionUseCase>();
        services.AddSingleton<IBuildPoissonProblemUseCase, BuildPoissonProblemUseCase>();
        services.AddSingleton<IStepPoissonSolverUseCase, StepPoissonSolverUseCase>();
        services.AddSingleton<IRunPoissonSolverUseCase, RunPoissonSolverUseCase>();
        services.AddSingleton<IExportPoissonImageUseCase, ExportPoissonImageUseCase>();
        services.AddSingleton<IExportPoissonReportUseCase, ExportPoissonReportUseCase>();
        services.AddScoped<PoissonBlendingSession>();
    }

    /// <summary>登记 Spectral Art 无状态数值服务、平台适配器和窄用例；大频谱只由用例创建的 Session 独占。</summary>
    private static void AddSpectralArtServices(IServiceCollection services)
    {
        services.AddSingleton<RadialLogPowerBaseline>();
        services.AddSingleton<SpectralPatternNormalizer>();
        services.AddSingleton<SpectralPatternMapper>();
        services.AddSingleton<SpectralPatternPreviewProjector>();
        services.AddSingleton<SpectralAmplitudeWriter>();
        services.AddSingleton<SpectralArtReconstructor>();
        services.AddSingleton<SpectralArtDiagnostics>();
        services.AddSingleton<SpectralExportFactVerifier>();
        services.AddSingleton<ISpectralTextRasterizer, AvaloniaSpectralTextRasterizer>();
        services.AddSingleton<ISpectralArtRecipeSerializer, SpectralArtRecipeSerializer>();
        services.AddSingleton<ISpectralArtReportSerializer, SpectralArtReportSerializer>();
        services.AddSingleton<ISpectralArtSnapshotSerializer, SpectralArtSnapshotSerializer>();
        services.AddSingleton<IPrepareSpectralArtCarrierUseCase, PrepareSpectralArtCarrierUseCase>();
        services.AddSingleton<ICreateSpectralPatternUseCase, CreateSpectralPatternUseCase>();
        services.AddSingleton<IRenderSpectralArtUseCase, RenderSpectralArtUseCase>();
        services.AddSingleton<IExportSpectralArtImageUseCase, ExportSpectralArtImageUseCase>();
        services.AddSingleton<IImportSpectralArtRecipeUseCase, ImportSpectralArtRecipeUseCase>();
        services.AddSingleton<IExportSpectralArtRecipeUseCase, ExportSpectralArtRecipeUseCase>();
        services.AddSingleton<IExportSpectralArtReportUseCase, ExportSpectralArtReportUseCase>();
    }

    /// <summary>登记 Hybrid Image 无状态数值服务、严格协议、窄用例和每 Document Scope 独占 Session 所需依赖。</summary>
    private static void AddHybridImageServices(IServiceCollection services)
    {
        // 固定 Gaussian 与相似变换没有运行期变化点；使用 sealed singleton，避免为单一实现制造策略/工厂层。
        services.AddSingleton<HybridLumaProjector>();
        services.AddSingleton<SimilarityTransformSolver>();
        services.AddSingleton<AlignedImageSampler>();
        services.AddSingleton<HybridCropValidator>();
        services.AddSingleton<GaussianPlaneFilter>();
        services.AddSingleton<HybridImageComposer>();
        services.AddSingleton<HybridScaleProjector>();
        services.AddSingleton<HybridImageDiagnostics>();
        services.AddSingleton<HybridResourceEstimator>();
        services.AddSingleton<HybridRenderCoordinator>();
        services.AddSingleton<IHybridImageRecipeSerializer, HybridImageRecipeSerializer>();
        services.AddSingleton<IHybridImageReportSerializer, HybridImageReportSerializer>();
        services.AddSingleton<IHybridImageSnapshotSerializer, HybridImageSnapshotSerializer>();
        services.AddSingleton<IPrepareHybridInputsUseCase, PrepareHybridInputsUseCase>();
        services.AddSingleton<ISolveHybridAlignmentUseCase, SolveHybridAlignmentUseCase>();
        services.AddSingleton<IRenderHybridPreviewUseCase, RenderHybridPreviewUseCase>();
        services.AddSingleton<IRenderHybridFullSizeUseCase, RenderHybridFullSizeUseCase>();
        services.AddSingleton<IExportHybridImageUseCase, ExportHybridImageUseCase>();
        services.AddSingleton<IImportHybridRecipeUseCase, ImportHybridRecipeUseCase>();
        services.AddSingleton<IExportHybridRecipeUseCase, ExportHybridRecipeUseCase>();
        services.AddSingleton<IExportHybridReportUseCase, ExportHybridReportUseCase>();
    }

    /// <summary>登记幅相交换固定数值服务、严格协议与窄应用用例。</summary>
    private static void AddMagnitudePhaseServices(IServiceCollection services)
    {
        // 固定算法没有运行期替代实现，使用 sealed singleton；只在文件与层间边界保留接口。
        services.AddSingleton<FrequencyPairCanvasProjector>();
        services.AddSingleton<MagnitudePhaseSpectrumBuilder>();
        services.AddSingleton<MagnitudePhaseSpectrumProjector>();
        services.AddSingleton<SpectrumComponentMixer>();
        services.AddSingleton<MagnitudePhaseReconstructor>();
        services.AddSingleton<MagnitudePhaseDisplayProjector>();
        services.AddSingleton<MagnitudePhaseDiagnostics>();
        services.AddSingleton<MagnitudePhaseResourceEstimator>();
        services.AddSingleton<IMagnitudePhaseRecipeSerializer, MagnitudePhaseRecipeSerializer>();
        services.AddSingleton<IMagnitudePhaseReportSerializer, MagnitudePhaseReportSerializer>();
        services.AddSingleton<IMagnitudePhaseSnapshotSerializer, MagnitudePhaseSnapshotSerializer>();
        services.AddSingleton<IPrepareMagnitudePhasePairUseCase, PrepareMagnitudePhasePairUseCase>();
        services.AddSingleton<IRenderMagnitudePhaseExperimentUseCase, RenderMagnitudePhaseExperimentUseCase>();
        services.AddSingleton<IInspectMagnitudePhasePointUseCase, InspectMagnitudePhasePointUseCase>();
        services.AddSingleton<IExportMagnitudePhaseImageUseCase, ExportMagnitudePhaseImageUseCase>();
        services.AddSingleton<IImportMagnitudePhaseRecipeUseCase, ImportMagnitudePhaseRecipeUseCase>();
        services.AddSingleton<IExportMagnitudePhaseRecipeUseCase, ExportMagnitudePhaseRecipeUseCase>();
        services.AddSingleton<IExportMagnitudePhaseReportUseCase, ExportMagnitudePhaseReportUseCase>();
    }

    /// <summary>登记图像示波器固定数值服务和四个窄应用用例；大数组只由每个 Document 的 Session 独占。</summary>
    private static void AddImageOscilloscopeServices(IServiceCollection services)
    {
        // 固定颜色与坐标协议不存在运行期替换者，因此直接登记 sealed 服务，不制造策略目录或工厂。
        services.AddSingleton<OscilloscopeColorConverter>();
        services.AddSingleton<ImageOscilloscopeAnalyzer>();
        services.AddSingleton<ClippingAnalyzer>();
        services.AddSingleton<ImageOscilloscopePreviewProjector>();
        services.AddSingleton<ScopeDensityProjector>();
        services.AddSingleton<ImageOscilloscopeRasterizer>();
        services.AddSingleton<ScopeProbeMapper>();
        services.AddSingleton<ImageProbeCoordinateMapper>();
        services.AddSingleton<VectorscopeReferenceTargetProvider>();
        services.AddSingleton<IPrepareImageOscilloscopeSessionUseCase, PrepareImageOscilloscopeSessionUseCase>();
        services.AddSingleton<IRecalculateImageOscilloscopeClippingUseCase, RecalculateImageOscilloscopeClippingUseCase>();
        services.AddSingleton<IProjectImageOscilloscopeDisplayUseCase, ProjectImageOscilloscopeDisplayUseCase>();
        services.AddSingleton<IInspectImageOscilloscopePixelUseCase, InspectImageOscilloscopePixelUseCase>();
    }
}
