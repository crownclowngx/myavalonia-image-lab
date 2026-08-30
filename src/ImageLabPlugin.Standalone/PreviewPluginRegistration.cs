using Avalonia.Controls;
using ImageLabPlugin.Constants;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace ImageLabPlugin.Standalone;

/// <summary>Standalone 只捕获真实 Module 注册，不模拟 manifest、Dock、保存协调器或 Host 内部对象。</summary>
internal sealed class PreviewPluginRegistration : IPluginRegistration
{
    public PluginId PluginId => PluginIds.Plugin;
    public IServiceCollection Services { get; } = new ServiceCollection();

    public void UseLifecycle<TLifecycle>() where TLifecycle : class, IPluginLifecycle =>
        Services.AddSingleton<TLifecycle>();

    public void AddDocument<TDocument, TView>(DocumentDescriptor descriptor)
        where TDocument : class, IPluginDocument
        where TView : Control, new()
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        Services.AddScoped<TDocument>();
        Services.AddTransient<TView>();
    }

    public void AddPersistableDocument<TDocument, TView>(DocumentDescriptor descriptor)
        where TDocument : class, IPersistablePluginDocument
        where TView : Control, new()
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        Services.AddScoped<TDocument>();
        Services.AddTransient<TView>();
    }

    public void AddTool<TTool, TView>(ToolDescriptor descriptor)
        where TTool : class
        where TView : Control, new() =>
        throw new NotSupportedException("ImageLab V1 不登记 Host Tool。");
}
