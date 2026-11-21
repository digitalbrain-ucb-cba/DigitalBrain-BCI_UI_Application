using System.Windows;
using System.Windows.Media;
using DigitalBrain.UI.Models.Dsp;

namespace DigitalBrain.UI.Controls;

/// <summary>Espectros de dos canales enfrentados.</summary>
/// <param name="Left">Amplitud (µV) por bin del canal izquierdo, o <c>null</c>.</param>
/// <param name="Right">Amplitud (µV) por bin del canal derecho, o <c>null</c>.</param>
/// <param name="Scale">Amplitud que llena media anchura.</param>
/// <param name="LeftPeak">Frecuencia dominante del canal izquierdo (NaN si no hay).</param>
public sealed record SpectrumFrame(
    double[]? Left,
    double[]? Right,
    double FrequencyStep,
    double MaxFrequency,
    double Scale,
    double LeftPeak,
    double RightPeak);

/// <summary>
/// Espectro «en mariposa» como el de Signal Tester: el eje central es la frecuencia (0 abajo, máximo arriba) y cada canal
/// se dibuja hacia un lado con una barra por bin de la FFT, coloreada según su banda (delta, theta, alfa, beta, gamma).
/// </summary>
public sealed class SpectrumButterfly : PlotElement
{
    private const double AxisWidth = 40, BottomGutter = 22, TopPadding = 8, FrequencyLabelStep = 10;

    public static readonly DependencyProperty FrameProperty = DependencyProperty.Register(
        nameof(Frame), typeof(SpectrumFrame), typeof(SpectrumButterfly),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public SpectrumFrame? Frame { get => (SpectrumFrame?)GetValue(FrameProperty); set => SetValue(FrameProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth, height = ActualHeight;
        if (width < AxisWidth + 40 || height < BottomGutter + TopPadding + 30) return;

        var frame = Frame;
        double maxFrequency = frame?.MaxFrequency ?? 60;
        double center = width / 2;
        double plotTop = TopPadding, plotBottom = height - BottomGutter, plotHeight = plotBottom - plotTop;
        var left = new Rect(0, plotTop, center - AxisWidth / 2, plotHeight);
        var right = new Rect(center + AxisWidth / 2, plotTop, width - center - AxisWidth / 2, plotHeight);

        dc.DrawRoundedRectangle(PlotBackground, null, left, 6, 6);
        dc.DrawRoundedRectangle(PlotBackground, null, right, 6, 6);

        double Y(double frequency) => plotBottom - frequency / maxFrequency * plotHeight;

        // Cuadrícula: frecuencias cada 10 Hz y media escala de amplitud.
        var grid = FrozenPen(GridBrush, 1);
        for (double f = FrequencyLabelStep; f < maxFrequency; f += FrequencyLabelStep)
        {
            double y = Crisp(Y(f));
            dc.DrawLine(grid, new Point(left.Left, y), new Point(left.Right, y));
            dc.DrawLine(grid, new Point(right.Left, y), new Point(right.Right, y));
        }
        dc.DrawLine(grid, new Point(Crisp(left.Left + left.Width / 2), plotTop), new Point(Crisp(left.Left + left.Width / 2), plotBottom));
        dc.DrawLine(grid, new Point(Crisp(right.Left + right.Width / 2), plotTop), new Point(Crisp(right.Left + right.Width / 2), plotBottom));

        if (frame is not null && frame.Scale > 0)
        {
            DrawBars(dc, frame.Left, frame, left, mirrored: true, Y);
            DrawBars(dc, frame.Right, frame, right, mirrored: false, Y);
            DrawPeak(dc, frame.LeftPeak, left.Right, -1, maxFrequency, Y);
            DrawPeak(dc, frame.RightPeak, right.Left, 1, maxFrequency, Y);
        }

        // Eje de frecuencias en el centro.
        dc.PushOpacity(0.8);
        for (double f = 0; f <= maxFrequency + 1e-9; f += FrequencyLabelStep)
        {
            var label = Text(f.ToString("0", Es), 11, TextBrush);
            double y = Math.Clamp(Y(f) - label.Height / 2, 0, plotBottom - label.Height / 2);
            dc.DrawText(label, new Point(center - label.Width / 2, y));
        }
        var hz = Text("Hz", 10, TextBrush);
        dc.DrawText(hz, new Point(center - hz.Width / 2, plotBottom + 4));

        // Escala de amplitud debajo de cada lado.
        if (frame is not null && frame.Scale > 0)
        {
            DrawAmplitudeLabel(dc, frame.Scale, left.Left + 2, plotBottom, alignLeft: true);
            DrawAmplitudeLabel(dc, frame.Scale / 2, left.Left + left.Width / 2, plotBottom, alignLeft: null);
            DrawAmplitudeLabel(dc, frame.Scale / 2, right.Left + right.Width / 2, plotBottom, alignLeft: null);
            DrawAmplitudeLabel(dc, frame.Scale, right.Right - 2, plotBottom, alignLeft: false);
        }
        dc.Pop();
    }

    private void DrawBars(DrawingContext dc, double[]? amplitude, SpectrumFrame frame, Rect area, bool mirrored, Func<double, double> y)
    {
        if (amplitude is null) return;
        double binHeight = Math.Max(1, frame.FrequencyStep / frame.MaxFrequency * area.Height - 0.6);
        dc.PushClip(new RectangleGeometry(area, 6, 6));
        for (int k = 1; k < amplitude.Length; k++)
        {
            double f = k * frame.FrequencyStep;
            if (f > frame.MaxFrequency) break;
            double length = Math.Clamp(amplitude[k] / frame.Scale, 0, 1) * area.Width;
            if (length < 0.5) continue;
            double top = y(f) - binHeight / 2;
            var rect = mirrored
                ? new Rect(area.Right - length, top, length, binHeight)
                : new Rect(area.Left, top, length, binHeight);
            dc.DrawRectangle(BandBrush(EegBand.IndexOf(f)), null, rect);
        }
        dc.Pop();
    }

    /// <summary>Pequeño triángulo junto al eje en la frecuencia dominante.</summary>
    private void DrawPeak(DrawingContext dc, double frequency, double edge, int direction, double maxFrequency, Func<double, double> y)
    {
        if (double.IsNaN(frequency) || frequency <= 0 || frequency > maxFrequency) return;
        double py = y(frequency);
        var triangle = new StreamGeometry();
        using (var ctx = triangle.Open())
        {
            ctx.BeginFigure(new Point(edge, py), true, true);
            ctx.LineTo(new Point(edge - direction * 7, py - 5), true, false);
            ctx.LineTo(new Point(edge - direction * 7, py + 5), true, false);
        }
        triangle.Freeze();
        dc.DrawGeometry(AccentBrush, null, triangle);
    }

    private void DrawAmplitudeLabel(DrawingContext dc, double value, double x, double plotBottom, bool? alignLeft)
    {
        var label = Text(value.ToString(value < 10 ? "0.#" : "0", Es), 10.5, TextBrush);
        double left = alignLeft switch
        {
            true => x,
            false => x - label.Width,
            null => x - label.Width / 2,
        };
        dc.DrawText(label, new Point(left, plotBottom + 4));
    }
}
