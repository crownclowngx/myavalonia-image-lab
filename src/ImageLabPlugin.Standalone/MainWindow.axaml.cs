using Avalonia.Controls;
using ImageLabPlugin.Features.WatermarkEmbed;
using ImageLabPlugin.Features.WatermarkInspect;
using ImageLabPlugin.Features.SpectrumInspector;
using ImageLabPlugin.Features.ImageCompareLab;
using ImageLabPlugin.Features.RobustnessLab;
using ImageLabPlugin.Features.ImageFingerprint;
using ImageLabPlugin.Features.BitPlaneViewer;
using ImageLabPlugin.Features.LsbSteganographyLab;
using ImageLabPlugin.Features.ConvolutionPlayground;
using ImageLabPlugin.Features.WaveletLab;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.PluginSdk;

namespace ImageLabPlugin.Standalone;

public sealed partial class MainWindow : Window
{
    private IServiceScope? _embedScope;
    private IServiceScope? _inspectScope;
    private IServiceScope? _spectrumScope;
    private IServiceScope? _compareScope;
    private IServiceScope? _robustnessScope;
    private IServiceScope? _fingerprintScope;
    private IServiceScope? _bitPlaneScope;
    private IServiceScope? _lsbScope;
    private IServiceScope? _convolutionScope;
    private IServiceScope? _waveletScope;

    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(IServiceProvider services) : this()
    {
        _embedScope = services.CreateScope();
        _inspectScope = services.CreateScope();
        _spectrumScope = services.CreateScope();
        _compareScope = services.CreateScope();
        _robustnessScope = services.CreateScope();
        _fingerprintScope = services.CreateScope();
        _bitPlaneScope = services.CreateScope();
        _lsbScope = services.CreateScope();
        _convolutionScope = services.CreateScope();
        _waveletScope = services.CreateScope();
        var embedDocument = _embedScope.ServiceProvider.GetRequiredService<WatermarkEmbedDocument>();
        var embedView = _embedScope.ServiceProvider.GetRequiredService<WatermarkEmbedView>();
        var inspectDocument = _inspectScope.ServiceProvider.GetRequiredService<WatermarkInspectDocument>();
        var inspectView = _inspectScope.ServiceProvider.GetRequiredService<WatermarkInspectView>();
        var spectrumDocument = _spectrumScope.ServiceProvider.GetRequiredService<SpectrumInspectorDocument>();
        var spectrumView = _spectrumScope.ServiceProvider.GetRequiredService<SpectrumInspectorView>();
        var compareDocument = _compareScope.ServiceProvider.GetRequiredService<ImageCompareLabDocument>();
        var compareView = _compareScope.ServiceProvider.GetRequiredService<ImageCompareLabView>();
        var robustnessDocument = _robustnessScope.ServiceProvider.GetRequiredService<RobustnessLabDocument>();
        var robustnessView = _robustnessScope.ServiceProvider.GetRequiredService<RobustnessLabView>();
        var fingerprintDocument = _fingerprintScope.ServiceProvider.GetRequiredService<ImageFingerprintDocument>();
        var fingerprintView = _fingerprintScope.ServiceProvider.GetRequiredService<ImageFingerprintView>();
        var bitPlaneDocument = _bitPlaneScope.ServiceProvider.GetRequiredService<BitPlaneViewerDocument>();
        var bitPlaneView = _bitPlaneScope.ServiceProvider.GetRequiredService<BitPlaneViewerView>();
        var lsbDocument = _lsbScope.ServiceProvider.GetRequiredService<LsbSteganographyLabDocument>();
        var lsbView = _lsbScope.ServiceProvider.GetRequiredService<LsbSteganographyLabView>();
        var convolutionDocument = _convolutionScope.ServiceProvider.GetRequiredService<ConvolutionPlaygroundDocument>();
        var convolutionView = _convolutionScope.ServiceProvider.GetRequiredService<ConvolutionPlaygroundView>();
        var waveletDocument = _waveletScope.ServiceProvider.GetRequiredService<WaveletLabDocument>();
        var waveletView = _waveletScope.ServiceProvider.GetRequiredService<WaveletLabView>();
        embedDocument.InitializeAsync(new NewDocumentActivation("水印写入"), CancellationToken.None)
            .AsTask().GetAwaiter().GetResult();
        inspectDocument.InitializeAsync(new NewDocumentActivation("提取与验证"), CancellationToken.None)
            .AsTask().GetAwaiter().GetResult();
        spectrumDocument.InitializeAsync(new NewDocumentActivation("频域分析器"), CancellationToken.None)
            .AsTask().GetAwaiter().GetResult();
        compareDocument.InitializeAsync(new NewDocumentActivation("图像比较实验室"), CancellationToken.None)
            .AsTask().GetAwaiter().GetResult();
        robustnessDocument.InitializeAsync(new NewDocumentActivation("鲁棒性实验室"), CancellationToken.None)
            .AsTask().GetAwaiter().GetResult();
        fingerprintDocument.InitializeAsync(new NewDocumentActivation("感知指纹"), CancellationToken.None)
            .AsTask().GetAwaiter().GetResult();
        bitPlaneDocument.InitializeAsync(new NewDocumentActivation("位平面观察器"), CancellationToken.None)
            .AsTask().GetAwaiter().GetResult();
        lsbDocument.InitializeAsync(new NewDocumentActivation("LSB 隐写与统计实验"), CancellationToken.None)
            .AsTask().GetAwaiter().GetResult();
        convolutionDocument.InitializeAsync(new NewDocumentActivation("卷积核实验台"), CancellationToken.None)
            .AsTask().GetAwaiter().GetResult();
        waveletDocument.InitializeAsync(new NewDocumentActivation("小波实验室"), CancellationToken.None)
            .AsTask().GetAwaiter().GetResult();
        embedView.DataContext = embedDocument;
        inspectView.DataContext = inspectDocument;
        EmbedPreview.Content = embedView;
        InspectPreview.Content = inspectView;
        spectrumView.DataContext = spectrumDocument;
        SpectrumPreview.Content = spectrumView;
        compareView.DataContext = compareDocument;
        ComparePreview.Content = compareView;
        robustnessView.DataContext = robustnessDocument;
        RobustnessPreview.Content = robustnessView;
        fingerprintView.DataContext = fingerprintDocument;
        FingerprintPreview.Content = fingerprintView;
        bitPlaneView.DataContext = bitPlaneDocument;
        BitPlanePreview.Content = bitPlaneView;
        lsbView.DataContext = lsbDocument;
        LsbPreview.Content = lsbView;
        convolutionView.DataContext = convolutionDocument;
        ConvolutionPreview.Content = convolutionView;
        waveletView.DataContext = waveletDocument;
        WaveletPreview.Content = waveletView;
        Closed += (_, _) =>
        {
            embedDocument.Dispose();
            inspectDocument.Dispose();
            spectrumDocument.Dispose();
            compareDocument.Dispose();
            robustnessDocument.Dispose();
            fingerprintDocument.Dispose();
            bitPlaneDocument.Dispose();
            lsbDocument.Dispose();
            convolutionDocument.Dispose();
            waveletDocument.Dispose();
            _embedScope?.Dispose();
            _inspectScope?.Dispose();
            _spectrumScope?.Dispose();
            _compareScope?.Dispose();
            _robustnessScope?.Dispose();
            _fingerprintScope?.Dispose();
            _bitPlaneScope?.Dispose();
            _lsbScope?.Dispose();
            _convolutionScope?.Dispose();
            _waveletScope?.Dispose();
            _embedScope = null;
            _inspectScope = null;
            _spectrumScope = null;
            _compareScope = null;
            _robustnessScope = null;
            _fingerprintScope = null;
            _bitPlaneScope = null;
            _lsbScope = null;
            _convolutionScope = null;
            _waveletScope = null;
        };
    }
}
