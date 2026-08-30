using Avalonia.Controls;

namespace ImageLabPlugin.Features.RobustnessLab;

/// <summary>纯布局视图；所有实验动作经绑定命令进入 Document 和应用用例。</summary>
public sealed partial class RobustnessLabView : UserControl
{
    public RobustnessLabView() => InitializeComponent();
}
