using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ImageLabPlugin.Domain.PoissonBlending;

namespace ImageLabPlugin.Features.PoissonBlending;

/// <summary>绘制有限残差 DTO 的 log10 RMS 曲线；零值固定投影到 -15，避免 log10(0)。</summary>
internal sealed class PoissonConvergenceChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<PoissonResidual>?> ResidualsProperty = AvaloniaProperty.Register<PoissonConvergenceChart, IReadOnlyList<PoissonResidual>?>(nameof(Residuals));
    static PoissonConvergenceChart() => AffectsRender<PoissonConvergenceChart>(ResidualsProperty);
    public IReadOnlyList<PoissonResidual>? Residuals { get => GetValue(ResidualsProperty); set => SetValue(ResidualsProperty, value); }
    public override void Render(DrawingContext context)
    {
        base.Render(context); context.FillRectangle(new SolidColorBrush(Color.FromRgb(24, 24, 28)), Bounds);
        if (Residuals is not { Count: > 1 } values) return; var geometry = new StreamGeometry();
        var logs = values.Select(item => Math.Log10(Math.Max(item.Rms, 1e-15))).ToArray(); var min = logs.Min(); var max = logs.Max(); var span = Math.Max(max - min, 1e-12);
        using (var sink = geometry.Open()) for (var i = 0; i < logs.Length; i++)
        { var point = new Point(i / (double)(logs.Length - 1) * Bounds.Width, (1d - ((logs[i] - min) / span)) * Bounds.Height); if (i == 0) sink.BeginFigure(point, false); else sink.LineTo(point); }
        context.DrawGeometry(null, new Pen(Brushes.DodgerBlue, 2), geometry);
    }
}
