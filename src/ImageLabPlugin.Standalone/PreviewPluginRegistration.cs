using Avalonia.Controls;
using ImageLabPlugin.Constants;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace ImageLabPlugin.Standalone;

/// <summary>Standalone 只捕获真实 Module 注册，不模拟 manifest、Dock、保存协调器或 Host 内部对象。</summary>
internal sealed class PreviewPluginRegistration : IPluginRegistration, IWorkflowActionRegistration
{
    public PluginId PluginId => PluginIds.Plugin;
    public IServiceCollection Services { get; } = new ServiceCollection();

    /// <summary>
    /// Module 的无 UI Provider 也必须能在预览容器中注册，否则启动阶段就会被 SDK 扩展接口拒绝。
    /// 这里只复用真实 scoped Handler 的对象图，不模拟 Host 的目录、授权或跨插件调用。
    /// </summary>
    public void AddWorkflowAction<THandler>(WorkflowActionDescriptor descriptor) where THandler : class, IWorkflowActionHandler
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        Services.AddScoped<THandler>();
    }

    public void UseWorkflowActionGateway() => throw new NotSupportedException("ImageLab 预览只提供 Provider 注册，不提供 Consumer Gateway。");

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
