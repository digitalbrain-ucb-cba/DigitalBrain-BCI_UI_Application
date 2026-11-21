namespace DigitalBrain.UI.Models.Dsp;

/// <summary>
/// Filtro IIR de segundo orden (forma directa II transpuesta) con los coeficientes del «Audio EQ Cookbook» de R. Bristow-Johnson.
/// Encadenando secciones con los factores Q de Butterworth se obtienen filtros de orden mayor sin rizado en la banda de paso.
/// </summary>
public sealed class Biquad
{
    /// <summary>Q de las dos secciones de un Butterworth de 4.º orden.</summary>
    public static readonly double[] ButterworthQ4 = [0.5411961, 1.3065630];

    private readonly double _b0, _b1, _b2, _a1, _a2;
    private double _z1, _z2;

    private Biquad(double b0, double b1, double b2, double a0, double a1, double a2)
    {
        _b0 = b0 / a0;
        _b1 = b1 / a0;
        _b2 = b2 / a0;
        _a1 = a1 / a0;
        _a2 = a2 / a0;
    }

    public static Biquad HighPass(double sampleRate, double cutoff, double q)
    {
        var (cos, alpha) = Prewarp(sampleRate, cutoff, q);
        return new Biquad((1 + cos) / 2, -(1 + cos), (1 + cos) / 2, 1 + alpha, -2 * cos, 1 - alpha);
    }

    public static Biquad LowPass(double sampleRate, double cutoff, double q)
    {
        var (cos, alpha) = Prewarp(sampleRate, cutoff, q);
        return new Biquad((1 - cos) / 2, 1 - cos, (1 - cos) / 2, 1 + alpha, -2 * cos, 1 - alpha);
    }

    /// <summary>Elimina una banda estrecha alrededor de <paramref name="frequency"/> (ancho ≈ frecuencia / Q).</summary>
    public static Biquad Notch(double sampleRate, double frequency, double q)
    {
        var (cos, alpha) = Prewarp(sampleRate, frequency, q);
        return new Biquad(1, -2 * cos, 1, 1 + alpha, -2 * cos, 1 - alpha);
    }

    public double Process(double x)
    {
        double y = _b0 * x + _z1;
        _z1 = _b1 * x - _a1 * y + _z2;
        _z2 = _b2 * x - _a2 * y;
        return y;
    }

    /// <summary>
    /// Deja el filtro en el estado estacionario de una entrada constante <paramref name="x"/>. Sin esto, el desplazamiento
    /// de continua del amplificador (decenas o cientos de µV) produce un transitorio largo al arrancar un pasa altos.
    /// </summary>
    public void Prime(double x)
    {
        double gain = (_b0 + _b1 + _b2) / (1 + _a1 + _a2);
        double y = gain * x;
        _z1 = y - _b0 * x;
        _z2 = _b2 * x - _a2 * y;
    }

    private static (double Cos, double Alpha) Prewarp(double sampleRate, double frequency, double q)
    {
        if (sampleRate <= 0 || frequency <= 0 || frequency >= sampleRate / 2)
            throw new ArgumentOutOfRangeException(nameof(frequency), "La frecuencia debe estar entre 0 y la mitad de la frecuencia de muestreo.");
        double w0 = 2 * Math.PI * frequency / sampleRate;
        return (Math.Cos(w0), Math.Sin(w0) / (2 * q));
    }
}
