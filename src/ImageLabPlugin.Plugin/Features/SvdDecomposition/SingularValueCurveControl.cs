using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace ImageLabPlugin.Features.SvdDecomposition;

/// <summary>绘制相对奇异值与累计能量，并只向外提交曲线索引意图。</summary>
/// <remarks>
/// 控件不持有 Session、不计算 SVD，也不直接改变 Document。蓝色实线表示累计能量，橙色虚线表示
/// σᵢ/σ₁；同时使用线型和颜色，避免只依赖红绿差异。鼠标与键盘都只产生 0-based 点索引。
/// </remarks>
public sealed class SingularValueCurveControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> SingularValuesProperty =
        AvaloniaProperty.Register<SingularValueCurveControl, IReadOnlyList<double>?>(nameof(SingularValues));
    public static readonly StyledProperty<IReadOnlyList<double>?> CumulativeEnergyProperty =
        AvaloniaProperty.Register<SingularValueCurveControl, IReadOnlyList<double>?>(nameof(CumulativeEnergy));
    public static readonly StyledProperty<int> SelectedIndexProperty =
        AvaloniaProperty.Register<SingularValueCurveControl, int>(nameof(SelectedIndex));
    public static readonly StyledProperty<bool> UseLogScaleProperty =
        AvaloniaProperty.Register<SingularValueCurveControl, bool>(nameof(UseLogScale), true);

    static SingularValueCurveControl()
    {
        AffectsRender<SingularValueCurveControl>(SingularValuesProperty, CumulativeEnergyProperty,
            SelectedIndexProperty, UseLogScaleProperty);
        FocusableProperty.OverrideDefaultValue<SingularValueCurveControl>(true);
    }

    public IReadOnlyList<double>? SingularValues { get => GetValue(SingularValuesProperty); set => SetValue(SingularValuesProperty, value); }
    public IReadOnlyList<double>? CumulativeEnergy { get => GetValue(CumulativeEnergyProperty); set => SetValue(CumulativeEnergyProperty, value); }
    public int SelectedIndex { get => GetValue(SelectedIndexProperty); set => SetValue(SelectedIndexProperty, value); }
    public bool UseLogScale { get => GetValue(UseLogScaleProperty); set => SetValue(UseLogScaleProperty, value); }
    public event EventHandler<int>? PointSelected;

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        const double padding = 14d;
        var area = new Rect(padding, padding, Math.Max(0d, Bounds.Width - 2d * padding), Math.Max(0d, Bounds.Height - 2d * padding));
        var axis = new Pen(Brushes.Gray, 1d);
        context.DrawLine(axis, area.BottomLeft, area.BottomRight);
        context.DrawLine(axis, area.TopLeft, area.BottomLeft);
        var values = SingularValues;
        if (values is null || values.Count == 0 || area.Width <= 0d || area.Height <= 0d) return;
        var maximum = values[0];
        var minimumPositive = values.Where(value => value > 0d).DefaultIfEmpty(maximum).Min();
        var sigmaPen = new Pen(Brushes.DarkOrange, 2d, dashStyle: DashStyle.Dash);
        var energyPen = new Pen(Brushes.DodgerBlue, 2d);
        Point Map(int index, double normalized) => new(area.X + (values.Count == 1 ? area.Width / 2d : area.Width * index / (values.Count - 1d)),
            area.Bottom - area.Height * Math.Clamp(normalized, 0d, 1d));
        double NormalizeSigma(double value)
        {
            if (maximum <= 0d) return 0d;
            if (!UseLogScale) return value / maximum;
            if (minimumPositive >= maximum) return value > 0d ? 1d : 0d;
            return value <= 0d ? 0d : (Math.Log(value) - Math.Log(minimumPositive)) /
                (Math.Log(maximum) - Math.Log(minimumPositive));
        }
        DrawSeries(context, values.Count, index => Map(index, NormalizeSigma(values[index])), sigmaPen);
        if (CumulativeEnergy is { Count: > 0 } energy)
            DrawSeries(context, Math.Min(values.Count, energy.Count), index => Map(index, energy[index]), energyPen);
        var selected = Math.Clamp(SelectedIndex, 0, values.Count - 1);
        var selectedX = Map(selected, 0d).X;
        context.DrawLine(new Pen(Brushes.Black, 1d), new Point(selectedX, area.Top), new Point(selectedX, area.Bottom));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var values = SingularValues;
        if (values is null || values.Count == 0 || Bounds.Width <= 28d) return;
        var index = MapIndex(Bounds.Width, e.GetPosition(this).X, values.Count);
        Select(index);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var count = SingularValues?.Count ?? 0;
        if (count == 0) return;
        var target = e.Key switch
        {
            Key.Left => Math.Max(0, SelectedIndex - 1),
            Key.Right => Math.Min(count - 1, SelectedIndex + 1),
            Key.Home => 0,
            Key.End => count - 1,
            _ => -1
        };
        if (target < 0) return;
        Select(target);
        e.Handled = true;
    }

    private void Select(int index)
    {
        SelectedIndex = index;
        PointSelected?.Invoke(this, index);
    }

    internal static int MapIndex(double width, double x, int count)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (count == 1 || width <= 28d) return 0;
        var plotX = Math.Clamp(x - 14d, 0d, width - 28d);
        return (int)Math.Round(plotX / (width - 28d) * (count - 1));
    }

    private static void DrawSeries(DrawingContext context, int count, Func<int, Point> map, Pen pen)
    {
        if (count == 1) { var point = map(0); context.DrawEllipse(pen.Brush, null, point, 3d, 3d); return; }
        var previous = map(0);
        for (var index = 1; index < count; index++)
        { var current = map(index); context.DrawLine(pen, previous, current); previous = current; }
    }
}
