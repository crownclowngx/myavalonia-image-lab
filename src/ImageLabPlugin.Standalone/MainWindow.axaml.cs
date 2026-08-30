using Avalonia.Controls;
using ImageLabPlugin.Features.WatermarkEmbed;
using ImageLabPlugin.Features.WatermarkInspect;
using ImageLabPlugin.Features.SpectrumInspector;
using ImageLabPlugin.Features.ImageCompareLab;
using ImageLabPlugin.Features.RobustnessLab;
using ImageLabPlugin.Features.ImageFingerprint;
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
        Closed += (_, _) =>
        {
            embedDocument.Dispose();
            inspectDocument.Dispose();
            spectrumDocument.Dispose();
            compareDocument.Dispose();
            robustnessDocument.Dispose();
            fingerprintDocument.Dispose();
            _embedScope?.Dispose();
            _inspectScope?.Dispose();
            _spectrumScope?.Dispose();
            _compareScope?.Dispose();
            _robustnessScope?.Dispose();
            _fingerprintScope?.Dispose();
            _embedScope = null;
            _inspectScope = null;
            _spectrumScope = null;
            _compareScope = null;
            _robustnessScope = null;
            _fingerprintScope = null;
        };
    }
}
