using Avalonia.Controls;

namespace ImageLabPlugin.Features.BitPlaneViewer;

/// <summary>位平面观察器视图；只加载 AXAML，交互状态和命令均归 Document 所有。</summary>
public sealed partial class BitPlaneViewerView : UserControl
{
    public BitPlaneViewerView() => InitializeComponent();
}
