using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace ImageLabPlugin.Features.WaveletLab;

/// <summary>以 Uniform 方式显示有界小波投影，并在空态画出冻结的 LL/LH/HL/HH 四象限提示。</summary>
/// <remarks>控件不读取系数、不执行归一化；它只消费 Document 已拥有的 Bitmap，保持 UI 与数学职责分离。</remarks>
public sealed class WaveletPyramidControl : Control
{
    public static readonly StyledProperty<Bitmap?> SourceProperty =
        AvaloniaProperty.Register<WaveletPyramidControl, Bitmap?>(nameof(Source));

    static WaveletPyramidControl() => AffectsRender<WaveletPyramidControl>(SourceProperty);
    public Bitmap? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Source is not null)
        {
            var scale = Math.Min(Bounds.Width / Source.PixelSize.Width, Bounds.Height / Source.PixelSize.Height);
            var size = new Size(Source.PixelSize.Width * scale, Source.PixelSize.Height * scale);
            var destination = new Rect((Bounds.Width - size.Width) / 2d, (Bounds.Height - size.Height) / 2d, size.Width, size.Height);
            context.DrawImage(Source, new Rect(Source.Size), destination);
            return;
        }

        var halfWidth = Bounds.Width / 2d; var halfHeight = Bounds.Height / 2d;
        context.FillRectangle(Brushes.DimGray, new Rect(0, 0, halfWidth, halfHeight));
        context.FillRectangle(Brushes.SlateGray, new Rect(0, halfHeight, halfWidth, halfHeight));
        context.FillRectangle(Brushes.Gray, new Rect(halfWidth, 0, halfWidth, halfHeight));
        context.FillRectangle(Brushes.DarkGray, new Rect(halfWidth, halfHeight, halfWidth, halfHeight));
        var pen = new Pen(Brushes.White, 1d);
        context.DrawLine(pen, new(halfWidth, 0), new(halfWidth, Bounds.Height));
        context.DrawLine(pen, new(0, halfHeight), new(Bounds.Width, halfHeight));
    }
}
