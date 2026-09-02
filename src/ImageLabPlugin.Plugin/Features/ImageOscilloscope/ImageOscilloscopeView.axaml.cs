using Avalonia.Controls;
using Avalonia.Input;

namespace ImageLabPlugin.Features.ImageOscilloscope;

/// <summary>视图只转发 Pointer 位置、控件尺寸和 pin 意图；颜色、letterbox 与 Scope 映射都不在 code-behind。</summary>
public sealed partial class ImageOscilloscopeView : UserControl
{
    public ImageOscilloscopeView() => InitializeComponent();

    private void OnSourcePointerMoved(object? sender, PointerEventArgs eventArgs) => Forward(eventArgs, pin: false);

    private void OnSourcePointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        Forward(eventArgs, pin: true);
        SourceViewport.Focus();
    }

    private void OnSourcePointerExited(object? sender, PointerEventArgs eventArgs)
    {
        if (DataContext is ImageOscilloscopeDocument document) document.LeaveSourcePreview();
    }

    private void Forward(PointerEventArgs eventArgs, bool pin)
    {
        if (DataContext is not ImageOscilloscopeDocument document) return;
        var point = eventArgs.GetPosition(SourceViewport);
        document.UpdatePointer(point.X, point.Y, SourceViewport.Bounds.Width, SourceViewport.Bounds.Height, pin);
    }
}
