using Avalonia.Controls;

namespace ImageLabPlugin.Features.PeriodicNoiseRemoval;

/// <summary>周期噪声视图只连接声明式布局与频谱单击意图。</summary>
/// <remarks>坐标映射、共轭成对、草案状态、算法和文件副作用都由控件之外的对应职责处理。</remarks>
public sealed partial class PeriodicNoiseRemovalView : UserControl
{
    public PeriodicNoiseRemovalView()
    {
        InitializeComponent();
        OriginalSpectrum.FrequencySelected += OnFrequencySelected;
        FilteredSpectrum.FrequencySelected += OnFrequencySelected;
    }

    private void OnFrequencySelected(object? sender, PeriodicSpectrumSelectionEventArgs e)
    {
        if (DataContext is PeriodicNoiseRemovalDocument document) document.ToggleSpectrumPoint(e.X, e.Y);
    }
}
