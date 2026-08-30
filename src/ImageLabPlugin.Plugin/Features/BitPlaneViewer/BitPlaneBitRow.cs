using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageLabPlugin.Domain.BitPlanes;

namespace ImageLabPlugin.Features.BitPlaneViewer;

/// <summary>把一个 bit 的选择状态与只读统计组合成 UI 行。</summary>
internal sealed partial class BitPlaneBitRow : ObservableObject
{
    private readonly Action<int, bool> _selectionChanged;
    private bool _updating;

    public BitPlaneBitRow(int bitIndex, bool selected, BitPlaneStatistics? statistics, Action<int, bool> selectionChanged, Action<int> focus)
    {
        BitIndex = bitIndex;
        Weight = 1 << bitIndex;
        _isSelected = selected;
        Statistics = statistics;
        _selectionChanged = selectionChanged;
        FocusCommand = new RelayCommand(() => focus(BitIndex));
    }

    public int BitIndex { get; }
    public int Weight { get; }
    public string Name => BitIndex switch { 7 => "bit 7（MSB）", 0 => "bit 0（LSB）", _ => $"bit {BitIndex}" };
    public BitPlaneStatistics? Statistics { get; }
    public IRelayCommand FocusCommand { get; }
    public string StatisticsText => Statistics is null
        ? "尚无统计"
        : $"0: {Statistics.ZeroCount:N0}  1: {Statistics.OneCount:N0}  比例 {Statistics.OneRatio:P2}  熵 {Statistics.BinaryEntropy:F4}";

    [ObservableProperty] private bool _isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        if (!_updating) _selectionChanged(BitIndex, value);
    }

    public void Synchronize(bool selected)
    {
        _updating = true;
        try { IsSelected = selected; }
        finally { _updating = false; }
    }
}
