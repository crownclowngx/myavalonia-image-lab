using Avalonia.Controls;

namespace ImageLabPlugin.Features.FrequencyMaskEditor;

public sealed partial class FrequencyMaskEditorView : UserControl
{
    public FrequencyMaskEditorView()
    {
        InitializeComponent();
        MaskCanvas.GestureCompleted += OnGestureCompleted;
        MaskCanvas.Hovered += OnHovered;
    }

    private void OnGestureCompleted(object? sender, FrequencyMaskGestureEventArgs e)
    {
        if (DataContext is FrequencyMaskEditorDocument document) document.CommitGesture(e.Points);
    }

    private void OnHovered(object? sender, FrequencyMaskHoverEventArgs e)
    {
        if (DataContext is FrequencyMaskEditorDocument document) document.InspectAt(e.X, e.Y);
    }
}
