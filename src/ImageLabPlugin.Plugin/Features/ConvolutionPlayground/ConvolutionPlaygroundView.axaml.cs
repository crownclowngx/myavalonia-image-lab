using Avalonia.Controls;

namespace ImageLabPlugin.Features.ConvolutionPlayground;

/// <summary>只加载编译绑定布局；算法、文件和资源生命周期均由 Document/用例负责。</summary>
public sealed partial class ConvolutionPlaygroundView : UserControl
{
    public ConvolutionPlaygroundView() => InitializeComponent();
}
