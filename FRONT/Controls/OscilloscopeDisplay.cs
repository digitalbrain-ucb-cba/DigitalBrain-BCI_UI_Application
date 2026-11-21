using System.Windows;
using System.Windows.Media;
using DigitalBrain.UI.Models;

namespace DigitalBrain.UI.Controls;

/// <summary>Traza de un canal en una pantalla del osciloscopio.</summary>
/// <param name="Channel">0–3 (A–D): decide el color y la etiqueta.</param>
/// <param name="Values">µV por posición horizontal; NaN deja un hueco.</param>
/// <param name="Pulses">Posiciones en que el detector marcó un pulso.</param>
public sealed record ScopeTrace(int Channel, double[] Values, IReadOnlyList<int> Pulses);

/// <summary>Contenido de una pantalla del osciloscopio.</summary>
/// <param name="Saturated">Posiciones con algún canal en ±800 µV; puede ser <c>null</c>.</param>
/// <param name="Cursor">Posición del barrido (se dibuja una línea), o <c>null</c>.</param>
/// <param name="Trigger">Posición del disparo, o <c>null</c>.</param>
/// <param name="StartSeconds">Tiempo de la posición 0 para las etiquetas del eje horizontal.</param>
public sealed record ScopeFrame(
    IReadOnlyList<ScopeTrace> Traces,
    bool[]? Saturated,
    int Length,
    int? Cursor,
    int? Trigger,
    double StartSeconds,
    double SecondsPerDivision,
    double MicroVoltsPerDivision);

/// <summary>
/// Pantalla de osciloscopio de varios canales, como la de «Signal Tester»: una franja por canal con su propia línea base,
/// cuadrícula de 10 divisiones horizontales y 4 verticales por franja (±2 divisiones), barrido, disparo, pulsos y saturación.
/// Si hay más muestras que píxeles, dibuja el mínimo y el máximo de cada columna para no perder picos.
/// </summary>
public sealed class OscilloscopeDisplay : PlotElement
{
    public const int Divisions = 10;
    /// <summary>Divisiones verticales de cada franja: la escala completa es ±2 · µV/div.</summary>
    public const int LaneDivisions = 4;
    private const double LeftGutter = 74, BottomGutter = 24, TopPadding = 6;

    public static readonly DependencyProperty FrameProperty = DependencyProperty.Register(
        nameof(Frame), typeof(ScopeFrame), typeof(OscilloscopeDisplay),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty Channel1BrushProperty = RegisterBrush(nameof(Channel1Brush));
    public static readonly DependencyProperty Channel2BrushProperty = RegisterBrush(nameof(Channel2Brush));
    public static readonly DependencyProperty Channel3BrushProperty = RegisterBrush(nameof(Channel3Brush));
    public static readonly DependencyProperty Channel4BrushProperty = RegisterBrush(nameof(Channel4Brush));
    public static readonly DependencyProperty SaturationBrushProperty = RegisterBrush(nameof(SaturationBrush));

    public ScopeFrame? Frame { get => (ScopeFrame?)GetValue(FrameProperty); set => SetValue(FrameProperty, value); }
    public Brush Channel1Brush { get => (Brush)GetValue(Channel1BrushProperty); set => SetValue(Channel1BrushProperty, value); }
    public Brush Channel2Brush { get => (Brush)GetValue(Channel2BrushProperty); set => SetValue(Channel2BrushProperty, value); }
    public Brush Channel3Brush { get => (Brush)GetValue(Channel3BrushProperty); set => SetValue(Channel3BrushProperty, value); }
    public Brush Channel4Brush { get => (Brush)GetValue(Channel4BrushProperty); set => SetValue(Channel4BrushProperty, value); }
    public Brush SaturationBrush { get => (Brush)GetValue(SaturationBrushProperty); set => SetValue(SaturationBrushProperty, value); }

    private Brush ChannelBrush(int channel) => channel switch
    {
        0 => Channel1Brush,
        1 => Channel2Brush,
        2 => Channel3Brush,
        _ => Channel4Brush,
    };

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth, height = ActualHeight;
        var plot = new Rect(LeftGutter, TopPadding, Math.Max(0, width - LeftGutter - 8), Math.Max(0, height - BottomGutter - TopPadding));
        if (plot.Width < 20 || plot.Height < 20) return;

        var frame = Frame;
        var traces = frame?.Traces ?? [];
        int lanes = Math.Max(1, frame is null ? AcquisitionParameters.ChannelNames.Count : traces.Count);
        double laneHeight = plot.Height / lanes;

        dc.DrawRoundedRectangle(PlotBackground, null, plot, 6, 6);
        dc.PushClip(new RectangleGeometry(plot, 6, 6));
        if (frame is not null) DrawSaturation(dc, plot, frame);
        DrawGrid(dc, plot, lanes, laneHeight);

        if (frame is not null)
        {
            for (int lane = 0; lane < traces.Count; lane++)
                DrawTrace(dc, plot, LaneRect(plot, lane, laneHeight), traces[lane], frame);
            DrawCursorAndTrigger(dc, plot, frame);
        }
        dc.Pop();

        DrawLaneLabels(dc, plot, laneHeight, frame);
        DrawTimeLabels(dc, plot, frame);
    }

    private static Rect LaneRect(Rect plot, int lane, double laneHeight) =>
        new(plot.Left, plot.Top + lane * laneHeight, plot.Width, laneHeight);

    private static double XAt(Rect plot, int index, int length) =>
        plot.Left + (length <= 1 ? 0 : index * plot.Width / (length - 1));

    private void DrawGrid(DrawingContext dc, Rect plot, int lanes, double laneHeight)
    {
        var minor = FrozenPen(GridBrush, 1);
        var major = FrozenPen(GridMajorBrush, 1);
        var baseline = FrozenPen(GridMajorBrush, 1, dashed: true);

        for (int i = 1; i < Divisions; i++)
        {
            double x = Crisp(plot.Left + i * plot.Width / Divisions);
            dc.DrawLine(minor, new Point(x, plot.Top), new Point(x, plot.Bottom));
        }
        for (int lane = 0; lane < lanes; lane++)
        {
            double top = plot.Top + lane * laneHeight;
            for (int d = 1; d < LaneDivisions; d++)
            {
                double y = Crisp(top + d * laneHeight / LaneDivisions);
                dc.DrawLine(d == LaneDivisions / 2 ? baseline : minor, new Point(plot.Left, y), new Point(plot.Right, y));
            }
            if (lane > 0)
                dc.DrawLine(major, new Point(plot.Left, Crisp(top)), new Point(plot.Right, Crisp(top)));
        }
    }

    private void DrawSaturation(DrawingContext dc, Rect plot, ScopeFrame frame)
    {
        if (frame.Saturated is not { } saturated) return;
        var brush = SaturationBrush.Clone();
        brush.Opacity = 0.22;
        brush.Freeze();
        double halfStep = frame.Length <= 1 ? 1 : plot.Width / (frame.Length - 1) / 2;
        for (int i = 0; i < saturated.Length; i++)
        {
            if (!saturated[i]) continue;
            int start = i;
            while (i + 1 < saturated.Length && saturated[i + 1]) i++;
            double x0 = XAt(plot, start, frame.Length) - halfStep, x1 = XAt(plot, i, frame.Length) + halfStep;
            dc.DrawRectangle(brush, null, new Rect(x0, plot.Top, Math.Max(2, x1 - x0), plot.Height));
        }
    }

    private void DrawTrace(DrawingContext dc, Rect plot, Rect lane, ScopeTrace trace, ScopeFrame frame)
    {
        var values = trace.Values;
        int length = frame.Length;
        if (values.Length < length || length < 2) return;

        double mid = lane.Top + lane.Height / 2;
        double halfHeight = lane.Height / 2 - 2;
        double fullScale = Math.Max(frame.MicroVoltsPerDivision, 1e-6) * LaneDivisions / 2;
        double Y(double v) => mid - Math.Clamp(v / fullScale, -1, 1) * halfHeight;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            bool open = false;
            // Más de dos muestras por píxel: se dibuja una línea vertical mínimo–máximo por columna.
            if (length > plot.Width * 2)
            {
                int columns = (int)plot.Width;
                for (int c = 0; c < columns; c++)
                {
                    int from = (int)((long)c * length / columns), to = (int)((long)(c + 1) * length / columns);
                    double min = double.PositiveInfinity, max = double.NegativeInfinity;
                    for (int i = from; i < to; i++)
                    {
                        double v = values[i];
                        if (double.IsNaN(v)) continue;
                        if (v < min) min = v;
                        if (v > max) max = v;
                    }
                    if (double.IsInfinity(min))
                    {
                        open = false;
                        continue;
                    }
                    double x = plot.Left + c + 0.5;
                    if (!open)
                    {
                        ctx.BeginFigure(new Point(x, Y(max)), false, false);
                        open = true;
                    }
                    else
                    {
                        ctx.LineTo(new Point(x, Y(max)), true, true);
                    }
                    ctx.LineTo(new Point(x, Y(min)), true, true);
                }
            }
            else
            {
                for (int i = 0; i < length; i++)
                {
                    double v = values[i];
                    if (double.IsNaN(v))
                    {
                        open = false;
                        continue;
                    }
                    var point = new Point(XAt(plot, i, length), Y(v));
                    if (!open)
                    {
                        ctx.BeginFigure(point, false, false);
                        open = true;
                    }
                    else
                    {
                        ctx.LineTo(point, true, true);
                    }
                }
            }
        }
        geometry.Freeze();

        var brush = ChannelBrush(trace.Channel);
        var pen = new Pen(brush, 1.6) { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        dc.PushClip(new RectangleGeometry(lane));
        dc.DrawGeometry(null, pen, geometry);

        if (trace.Pulses.Count > 0)
        {
            var marker = FrozenPen(AccentBrush, 1, dashed: true);
            foreach (var index in trace.Pulses)
            {
                if (index < 0 || index >= length) continue;
                double x = XAt(plot, index, length);
                dc.DrawLine(marker, new Point(x, lane.Top + 9), new Point(x, lane.Bottom - 2));
                var triangle = new StreamGeometry();
                using (var ctx = triangle.Open())
                {
                    ctx.BeginFigure(new Point(x - 5, lane.Top + 2), true, true);
                    ctx.LineTo(new Point(x + 5, lane.Top + 2), true, false);
                    ctx.LineTo(new Point(x, lane.Top + 9), true, false);
                }
                triangle.Freeze();
                dc.DrawGeometry(AccentBrush, null, triangle);
            }
        }
        dc.Pop();
    }

    private void DrawCursorAndTrigger(DrawingContext dc, Rect plot, ScopeFrame frame)
    {
        if (frame.Cursor is { } cursor)
        {
            double x = Crisp(XAt(plot, cursor, frame.Length));
            dc.DrawLine(FrozenPen(AccentBrush, 1.5), new Point(x, plot.Top), new Point(x, plot.Bottom));
        }
        if (frame.Trigger is { } trigger)
        {
            double x = Crisp(XAt(plot, trigger, frame.Length));
            dc.DrawLine(FrozenPen(AccentBrush, 1.2, dashed: true), new Point(x, plot.Top), new Point(x, plot.Bottom));
            var label = Text("T", 11, AccentBrush, bold: true);
            var box = new Rect(x - 8, plot.Top + 2, 16, 16);
            dc.DrawRoundedRectangle(PlotBackground, FrozenPen(AccentBrush, 1), box, 3, 3);
            dc.DrawText(label, new Point(x - label.Width / 2, box.Top + (box.Height - label.Height) / 2));
        }
    }

    private void DrawLaneLabels(DrawingContext dc, Rect plot, double laneHeight, ScopeFrame? frame)
    {
        var names = AcquisitionParameters.ChannelNames;
        var channels = frame is null ? Enumerable.Range(0, names.Count).ToList() : frame.Traces.Select(t => t.Channel).ToList();
        string scale = frame is null ? "" : $"±{FormatMicroVolts(frame.MicroVoltsPerDivision * LaneDivisions / 2)}";
        for (int lane = 0; lane < channels.Count; lane++)
        {
            int channel = channels[lane];
            double mid = plot.Top + lane * laneHeight + laneHeight / 2;
            var name = Text($"CH-{names[Math.Clamp(channel, 0, names.Count - 1)]}", 13, TextBrush, bold: true);
            double nameTop = mid - name.Height / 2 - (scale.Length > 0 && laneHeight > 44 ? 7 : 0);
            dc.DrawRoundedRectangle(ChannelBrush(channel), null, new Rect(4, nameTop + name.Height / 2 - 4, 8, 8), 2, 2);
            dc.DrawText(name, new Point(16, nameTop));
            if (scale.Length > 0 && laneHeight > 44)
            {
                var range = Text(scale, 10.5, TextBrush);
                dc.PushOpacity(0.7);
                dc.DrawText(range, new Point(16, nameTop + name.Height));
                dc.Pop();
            }
        }
    }

    private void DrawTimeLabels(DrawingContext dc, Rect plot, ScopeFrame? frame)
    {
        double start = frame?.StartSeconds ?? 0;
        double perDivision = frame?.SecondsPerDivision ?? 0.5;
        dc.PushOpacity(0.75);
        for (int i = 0; i <= Divisions; i += Divisions > 10 ? 2 : 1)
        {
            double seconds = start + i * perDivision;
            string text = Math.Abs(seconds) < 1e-9 ? "0" : seconds.ToString("0.##", Es);
            if (i == Divisions) text += " s";
            var label = Text(text, 11, TextBrush);
            double x = plot.Left + i * plot.Width / Divisions - label.Width / 2;
            x = Math.Clamp(x, plot.Left - 4, plot.Right - label.Width + 6);
            dc.DrawText(label, new Point(x, plot.Bottom + 5));
        }
        dc.Pop();
    }

    private static string FormatMicroVolts(double value) => value >= 1000
        ? $"{(value / 1000).ToString("0.##", Es)} mV"
        : $"{value.ToString("0.#", Es)} µV";
}
