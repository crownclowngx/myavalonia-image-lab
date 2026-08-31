using Avalonia.Controls;

namespace ImageLabPlugin.Features.SvdDecomposition;

/// <summary>只负责加载布局，并把曲线的索引意图转交当前 Document。</summary>
public sealed partial class SvdDecompositionView : UserControl
{
    public SvdDecompositionView()
    {
        InitializeComponent();
        Curve.PointSelected += (_, index) =>
        {
            if (DataContext is SvdDecompositionDocument document) document.SelectCurvePoint(index);
        };
    }
}
