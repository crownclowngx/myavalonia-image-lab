using Avalonia.Controls;

namespace ImageLabPlugin.Features.HybridImage;

/// <summary>Hybrid Image 视图只加载 AXAML；业务计算和资源所有权均由 Document/用例负责。</summary>
public sealed partial class HybridImageView : UserControl
{
    public HybridImageView() => InitializeComponent();
}
