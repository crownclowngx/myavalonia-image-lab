using Avalonia.Controls;
using ImageLabPlugin.Features.WatermarkEmbed;
using ImageLabPlugin.Features.WatermarkInspect;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.PluginSdk;

namespace ImageLabPlugin.Standalone;

public sealed partial class MainWindow : Window
{
    private IServiceScope? _embedScope;
    private IServiceScope? _inspectScope;

    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(IServiceProvider services) : this()
    {
        _embedScope = services.CreateScope();
        _inspectScope = services.CreateScope();
        var embedDocument = _embedScope.ServiceProvider.GetRequiredService<WatermarkEmbedDocument>();
        var embedView = _embedScope.ServiceProvider.GetRequiredService<WatermarkEmbedView>();
        var inspectDocument = _inspectScope.ServiceProvider.GetRequiredService<WatermarkInspectDocument>();
        var inspectView = _inspectScope.ServiceProvider.GetRequiredService<WatermarkInspectView>();
        embedDocument.InitializeAsync(new NewDocumentActivation("水印写入"), CancellationToken.None)
            .AsTask().GetAwaiter().GetResult();
        inspectDocument.InitializeAsync(new NewDocumentActivation("提取与验证"), CancellationToken.None)
            .AsTask().GetAwaiter().GetResult();
        embedView.DataContext = embedDocument;
        inspectView.DataContext = inspectDocument;
        EmbedPreview.Content = embedView;
        InspectPreview.Content = inspectView;
        Closed += (_, _) =>
        {
            embedDocument.Dispose();
            inspectDocument.Dispose();
            _embedScope?.Dispose();
            _inspectScope?.Dispose();
            _embedScope = null;
            _inspectScope = null;
        };
    }
}
