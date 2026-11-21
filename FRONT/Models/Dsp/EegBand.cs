namespace DigitalBrain.UI.Models.Dsp;

/// <summary>
/// Banda de frecuencia del EEG. Theta, alfa, beta y gamma tienen los mismos límites que <c>FREQUENCY_BANDS</c> del backend
/// (gamma llega hasta Nyquist); delta se añade solo para la visualización, el modelo no la usa.
/// </summary>
/// <param name="BrushKey">Clave del color de la banda en los temas (BandDeltaBrush…).</param>
public sealed record EegBand(string Name, string Symbol, double Low, double High, string BrushKey)
{
    public static readonly IReadOnlyList<EegBand> All =
    [
        new("Delta", "δ", 0.5, 4, "BandDeltaBrush"),
        new("Theta", "θ", 4, 8, "BandThetaBrush"),
        new("Alfa", "α", 8, 13, "BandAlphaBrush"),
        new("Beta", "β", 13, 30, "BandBetaBrush"),
        new("Gamma", "γ", 30, double.PositiveInfinity, "BandGammaBrush"),
    ];

    public bool Contains(double frequency) => frequency >= Low && frequency < High;

    /// <summary>Índice en <see cref="All"/> de la banda que contiene la frecuencia; las inferiores a 0,5 Hz cuentan como delta.</summary>
    public static int IndexOf(double frequency)
    {
        for (int i = All.Count - 1; i >= 0; i--)
        {
            if (frequency >= All[i].Low) return i;
        }
        return 0;
    }

    public string RangeText => double.IsPositiveInfinity(High) ? $"≥ {Low:0.#} Hz" : $"{Low:0.#}–{High:0.#} Hz";
}
