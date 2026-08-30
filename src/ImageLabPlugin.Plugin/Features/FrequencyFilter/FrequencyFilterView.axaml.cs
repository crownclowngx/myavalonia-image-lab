using Avalonia.Controls;

namespace ImageLabPlugin.Features.FrequencyFilter;

/// <summary>频域滤波视图仅承载声明式布局；公式、文件和异步生命周期均由对应层负责。</summary>
public sealed partial class FrequencyFilterView : UserControl
{
    public FrequencyFilterView() => InitializeComponent();
}
