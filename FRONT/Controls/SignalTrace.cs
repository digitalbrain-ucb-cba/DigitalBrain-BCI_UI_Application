using System.Windows;
using System.Windows.Media;

namespace DigitalBrain.UI.Controls;

/// <summary>
/// Traza ligera de un canal: dibuja <see cref="Samples"/> como una línea suave, escalada a ±<see cref="Range"/>.
/// Se usa en lugar de un gráfico con ejes porque el diseño solo muestra la forma de onda.
/// </summary>
public sealed class SignalTrace : FrameworkElement
{
    public static readonly DependencyProperty SamplesProperty = DependencyProperty.Register(
        nameof(Samples), typeof(IReadOnlyList<double>), typeof(SignalTrace),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(SignalTrace),
        new FrameworkPropertyMetadata(Brushes.SteelBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RangeProperty = DependencyProperty.Register(
        nameof(Range), typeof(double), typeof(SignalTrace),
        new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BaselineBrushProperty = DependencyProperty.Register(
        nameof(BaselineBrush), typeof(Brush), typeof(SignalTrace),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double>? Samples
    {
        get => (IReadOnlyList<double>?)GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double Range
    {
        get => (double)GetValue(RangeProperty);
        set => SetValue(RangeProperty, value);
    }

    public Brush? BaselineBrush
    {
        get => (Brush?)GetValue(BaselineBrushProperty);
        set => SetValue(BaselineBrushProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth, height = ActualHeight;
        if (width <= 0 || height <= 0) return;

        double mid = height / 2;
        var samples = Samples;
        if (samples is null || samples.Count < 2)
        {
            if (BaselineBrush is not null)
                dc.DrawLine(new Pen(BaselineBrush, 1) { DashStyle = DashStyles.Dash }, new Point(0, mid), new Point(width, mid));
            return;
        }

        double range = Math.Max(Range, 1);
        double halfHeight = height / 2 - 3;
        double step = width / (samples.Count - 1);

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(PointAt(0), false, false);
            for (int i = 1; i < samples.Count; i++)
                ctx.LineTo(PointAt(i), true, true);
        }
        geometry.Freeze();

        var pen = new Pen(Stroke, 2) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, width, height)));
        dc.DrawGeometry(null, pen, geometry);
        dc.Pop();

        Point PointAt(int i) => new(i * step, mid - Math.Clamp(samples[i] / range, -1, 1) * halfHeight);
    }
}
