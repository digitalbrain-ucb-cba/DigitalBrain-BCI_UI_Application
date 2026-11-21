using System.Globalization;
using System.Windows;
using System.Windows.Media;
using DigitalBrain.UI.Models.Dsp;

namespace DigitalBrain.UI.Controls;

/// <summary>
/// Base de los gráficos del osciloscopio: colores que llegan del tema (con <c>DynamicResource</c>, así que el cambio de tema
/// redibuja aunque la señal esté detenida) y utilidades de texto.
/// </summary>
public abstract class PlotElement : FrameworkElement
{
    public static readonly DependencyProperty PlotBackgroundProperty = RegisterBrush(nameof(PlotBackground));
    public static readonly DependencyProperty GridBrushProperty = RegisterBrush(nameof(GridBrush));
    public static readonly DependencyProperty GridMajorBrushProperty = RegisterBrush(nameof(GridMajorBrush));
    public static readonly DependencyProperty TextBrushProperty = RegisterBrush(nameof(TextBrush));
    public static readonly DependencyProperty AccentBrushProperty = RegisterBrush(nameof(AccentBrush));
    public static readonly DependencyProperty DeltaBrushProperty = RegisterBrush(nameof(DeltaBrush));
    public static readonly DependencyProperty ThetaBrushProperty = RegisterBrush(nameof(ThetaBrush));
    public static readonly DependencyProperty AlphaBrushProperty = RegisterBrush(nameof(AlphaBrush));
    public static readonly DependencyProperty BetaBrushProperty = RegisterBrush(nameof(BetaBrush));
    public static readonly DependencyProperty GammaBrushProperty = RegisterBrush(nameof(GammaBrush));

    protected static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");
    private static readonly Typeface MonoTypeface = new(new FontFamily("Cascadia Mono, Consolas"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface BoldTypeface = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    public Brush PlotBackground { get => (Brush)GetValue(PlotBackgroundProperty); set => SetValue(PlotBackgroundProperty, value); }
    public Brush GridBrush { get => (Brush)GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public Brush GridMajorBrush { get => (Brush)GetValue(GridMajorBrushProperty); set => SetValue(GridMajorBrushProperty, value); }
    public Brush TextBrush { get => (Brush)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }
    public Brush AccentBrush { get => (Brush)GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }
    public Brush DeltaBrush { get => (Brush)GetValue(DeltaBrushProperty); set => SetValue(DeltaBrushProperty, value); }
    public Brush ThetaBrush { get => (Brush)GetValue(ThetaBrushProperty); set => SetValue(ThetaBrushProperty, value); }
    public Brush AlphaBrush { get => (Brush)GetValue(AlphaBrushProperty); set => SetValue(AlphaBrushProperty, value); }
    public Brush BetaBrush { get => (Brush)GetValue(BetaBrushProperty); set => SetValue(BetaBrushProperty, value); }
    public Brush GammaBrush { get => (Brush)GetValue(GammaBrushProperty); set => SetValue(GammaBrushProperty, value); }

    protected static DependencyProperty RegisterBrush(string name) => DependencyProperty.Register(
        name, typeof(Brush), typeof(PlotElement),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Color de la banda <paramref name="index"/> de <see cref="EegBand.All"/>.</summary>
    protected Brush BandBrush(int index) => index switch
    {
        0 => DeltaBrush,
        1 => ThetaBrush,
        2 => AlphaBrush,
        3 => BetaBrush,
        _ => GammaBrush,
    };

    protected FormattedText Text(string text, double size, Brush brush, bool bold = false) =>
        new(text, Es, FlowDirection.LeftToRight, bold ? BoldTypeface : MonoTypeface, size, brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

    protected static Pen FrozenPen(Brush brush, double thickness, bool dashed = false)
    {
        var pen = new Pen(brush, thickness);
        if (dashed) pen.DashStyle = new DashStyle([3, 3], 0);
        pen.Freeze();
        return pen;
    }

    /// <summary>Redondea una coordenada al centro del píxel para que las líneas de 1 px salgan nítidas.</summary>
    protected static double Crisp(double value) => Math.Round(value) + 0.5;
}
