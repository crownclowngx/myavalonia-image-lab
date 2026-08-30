using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ImageLabPlugin.Plugin;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace ImageLabPlugin.Standalone;

public sealed partial class App : Avalonia.Application
{
    private ServiceProvider? _provider;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var registration = new PreviewPluginRegistration();
            new ImageLabPluginModule().Configure(registration);
            registration.Services.AddSingleton<IPluginWindowInteraction, StandaloneWindowInteraction>();
            registration.Services.AddScoped<IDocumentLifetime, PreviewDocumentLifetime>();
            _provider = registration.Services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
            desktop.MainWindow = new MainWindow(_provider);
            desktop.Exit += (_, _) =>
            {
                _provider?.Dispose();
                _provider = null;
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
