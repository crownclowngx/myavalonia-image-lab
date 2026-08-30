using Avalonia.Controls;

namespace ImageLabPlugin.Features.LsbSteganographyLab;

/// <summary>只负责加载编译绑定 AXAML；所有状态、取消和资源所有权均在 Document。</summary>
public sealed partial class LsbSteganographyLabView : UserControl
{
    public LsbSteganographyLabView() => InitializeComponent();
}
