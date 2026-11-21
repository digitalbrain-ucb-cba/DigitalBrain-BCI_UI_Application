using System.Windows;
using System.Windows.Media;

namespace DigitalBrain.UI.Controls;

/// <summary>Barra apilada con la fracción de potencia de cada banda (delta → gamma), en los colores del espectro.</summary>
public sealed class BandPowerBar : PlotElement
{
    public static readonly DependencyProperty SharesProperty = DependencyProperty.Register(
        nameof(Shares), typeof(IReadOnlyList<double>), typeof(BandPowerBar),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double>? Shares { get => (IReadOnlyList<double>?)GetValue(SharesProperty); set => SetValue(SharesProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth, height = ActualHeight;
        if (width <= 0 || height <= 0) return;
        var outline = new RectangleGeometry(new Rect(0, 0, width, height), height / 2, height / 2);
        dc.DrawGeometry(PlotBackground, null, outline);

        var shares = Shares;
        double total = shares?.Sum() ?? 0;
        if (shares is null || total <= 0) return;

        dc.PushClip(outline);
        double x = 0;
        for (int i = 0; i < shares.Count; i++)
        {
            double w = shares[i] / total * width;
            if (w <= 0) continue;
            dc.DrawRectangle(BandBrush(i), null, new Rect(x, 0, w + 0.5, height));
            x += w;
        }
        dc.Pop();
    }
}
