namespace DigitalBrain.UI.Models.Dsp;

/// <summary>Filtros que se aplican a la señal antes de dibujarla y analizarla. <c>null</c> desactiva cada uno.</summary>
/// <param name="HighPassHz">Pasa altos Butterworth de 4.º orden: quita la continua y la deriva lenta de los electrodos.</param>
/// <param name="NotchHz">Rechazo de banda en la frecuencia de la red eléctrica (50 o 60 Hz).</param>
/// <param name="LowPassHz">Pasa bajos Butterworth de 4.º orden: quita ruido muscular y de alta frecuencia.</param>
public sealed record FilterSettings(double? HighPassHz, double? NotchHz, double? LowPassHz)
{
    /// <summary>Q del notch: a 50 Hz elimina unos ±1 Hz, sin tocar la banda gamma útil.</summary>
    public const double NotchQ = 25;

    public static FilterSettings Default { get; } = new(0.5, 50, 45);
}

/// <summary>
/// Acondiciona un canal: mantiene la última muestra válida mientras el amplificador está saturado (±800 µV), para que el salto
/// no haga oscilar los filtros, y aplica en cadena pasa altos, notch y pasa bajos. Los filtros que no caben por debajo de
/// Nyquist con la frecuencia de muestreo actual se omiten.
/// </summary>
public sealed class SignalConditioner
{
    private readonly Biquad[] _stages;
    private bool _primed;
    private double _lastValid;

    public SignalConditioner(double sampleRate, FilterSettings settings)
    {
        var nyquist = sampleRate / 2;
        var stages = new List<Biquad>();
        if (settings.HighPassHz is { } hp && hp > 0 && hp < nyquist)
            stages.AddRange(Biquad.ButterworthQ4.Select(q => Biquad.HighPass(sampleRate, hp, q)));
        if (settings.NotchHz is { } notch && notch > 0 && notch < nyquist)
            stages.Add(Biquad.Notch(sampleRate, notch, FilterSettings.NotchQ));
        if (settings.LowPassHz is { } lp && lp > 0 && lp < nyquist)
            stages.AddRange(Biquad.ButterworthQ4.Select(q => Biquad.LowPass(sampleRate, lp, q)));
        _stages = [.. stages];
    }

    public double Process(double raw, bool saturated)
    {
        double x = saturated && _primed ? _lastValid : raw;
        if (!saturated) _lastValid = raw;
        if (!_primed)
        {
            _primed = true;
            _lastValid = raw;
            double input = x;
            foreach (var stage in _stages)
            {
                stage.Prime(input);
                input = stage.Process(input);
            }
        }
        foreach (var stage in _stages)
            x = stage.Process(x);
        return x;
    }
}
