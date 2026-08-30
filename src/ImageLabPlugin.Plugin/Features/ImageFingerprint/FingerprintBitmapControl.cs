using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace ImageLabPlugin.Features.ImageFingerprint;

/// <summary>只绘制一个已计算的 64 位 8×8 指纹，并提供键盘可达的单元格说明。</summary>
internal sealed class FingerprintBitmapControl : Control
{
    public static readonly StyledProperty<ulong> BitsProperty = AvaloniaProperty.Register<FingerprintBitmapControl, ulong>(nameof(Bits));
    public static readonly StyledProperty<int> SelectedCellProperty = AvaloniaProperty.Register<FingerprintBitmapControl, int>(nameof(SelectedCell), 0);

    static FingerprintBitmapControl() => AffectsRender<FingerprintBitmapControl>(BitsProperty, SelectedCellProperty);

    public FingerprintBitmapControl()
    {
        Focusable = true;
        PointerPressed += (_, args) =>
        {
            var position = args.GetPosition(this);
            var side = Math.Min(Bounds.Width, Bounds.Height);
            if (side <= 0d) return;
            var x = Math.Clamp((int)(position.X / (side / 8d)), 0, 7);
            var y = Math.Clamp((int)(position.Y / (side / 8d)), 0, 7);
            SelectedCell = (y * 8) + x;
            Focus();
            UpdateAccessibleTip();
        };
    }

    public ulong Bits { get => GetValue(BitsProperty); set => SetValue(BitsProperty, value); }
    public int SelectedCell { get => GetValue(SelectedCellProperty); set => SetValue(SelectedCellProperty, Math.Clamp(value, 0, 63)); }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var side = Math.Min(Bounds.Width, Bounds.Height);
        var cell = side / 8d;
        for (var index = 0; index < 64; index++)
        {
            var x = index % 8; var y = index / 8;
            var isOne = (Bits & (1UL << (63 - index))) != 0;
            var rect = new Rect(x * cell, y * cell, cell, cell).Deflate(0.5d);
            context.FillRectangle(isOne ? Brushes.Black : Brushes.White, rect);
            context.DrawRectangle(new Pen(Brushes.Gray, 1), rect);
            if (index == SelectedCell) context.DrawRectangle(new Pen(Brushes.DodgerBlue, 2), rect.Deflate(1d));
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var next = e.Key switch
        {
            Key.Left => Math.Max(0, SelectedCell - 1),
            Key.Right => Math.Min(63, SelectedCell + 1),
            Key.Up => Math.Max(0, SelectedCell - 8),
            Key.Down => Math.Min(63, SelectedCell + 8),
            _ => SelectedCell
        };
        if (next != SelectedCell) { SelectedCell = next; UpdateAccessibleTip(); e.Handled = true; }
        base.OnKeyDown(e);
    }

    private void UpdateAccessibleTip()
    {
        var value = (Bits & (1UL << (63 - SelectedCell))) != 0 ? 1 : 0;
        ToolTip.SetTip(this, $"第 {(SelectedCell / 8) + 1} 行第 {(SelectedCell % 8) + 1} 列：{value}");
        ToolTip.SetIsOpen(this, true);
    }
}
