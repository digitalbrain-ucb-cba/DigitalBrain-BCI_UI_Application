namespace DigitalBrain.UI.Models.Dsp;

/// <summary>Espectro de un canal: amplitud por frecuencia, potencia por banda y frecuencia dominante.</summary>
/// <param name="Amplitude">Amplitud de pico en µV de cada frecuencia (k · <see cref="FrequencyStep"/>), de 0 a Nyquist.</param>
/// <param name="Psd">Densidad espectral de potencia en µV²/Hz, una cara.</param>
/// <param name="BandPower">Potencia en µV² de cada banda de <see cref="EegBand.All"/>.</param>
/// <param name="DominantFrequency">Frecuencia de mayor amplitud entre 1 y 45 Hz, interpolada entre bins; NaN si no hay señal.</param>
public sealed record SpectrumResult(double[] Amplitude, double[] Psd, double FrequencyStep, double[] BandPower, double DominantFrequency)
{
    public double TotalPower => BandPower.Sum();

    /// <summary>Fracción 0–1 de la potencia total en cada banda.</summary>
    public double[] RelativeBandPower()
    {
        double total = TotalPower;
        return total > 0 ? BandPower.Select(p => p / total).ToArray() : new double[BandPower.Length];
    }
}

/// <summary>
/// Estimador de Welch: divide la señal en segmentos solapados al 50 %, quita la media de cada uno, aplica una ventana de Hann,
/// calcula su FFT y promedia las potencias. Promediar reduce la varianza del espectro frente a una sola FFT, y la ventana de Hann
/// reduce la fuga espectral que produce cortar la señal en trozos.
/// </summary>
public sealed class WelchSpectrum
{
    /// <summary>Rango en que se busca la frecuencia dominante: excluye la deriva lenta y la zona de la red eléctrica.</summary>
    public const double DominantLow = 1.0, DominantHigh = 45.0;

    private readonly double _sampleRate;
    private readonly int _segment;
    private readonly double[] _window;
    private readonly double _windowSum;
    private readonly double _windowEnergy;
    private readonly double[] _re, _im, _power;

    /// <param name="segmentLength">Muestras por segmento; potencia de dos. La resolución es <paramref name="sampleRate"/> / <paramref name="segmentLength"/>.</param>
    public WelchSpectrum(double sampleRate, int segmentLength)
    {
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (!Fft.IsPowerOfTwo(segmentLength) || segmentLength < 8)
            throw new ArgumentException("El segmento debe ser una potencia de dos de al menos 8 muestras.", nameof(segmentLength));
        _sampleRate = sampleRate;
        _segment = segmentLength;
        _window = new double[segmentLength];
        for (int i = 0; i < segmentLength; i++)
            _window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / segmentLength); // Hann periódica
        _windowSum = _window.Sum();
        _windowEnergy = _window.Sum(w => w * w);
        _re = new double[segmentLength];
        _im = new double[segmentLength];
        _power = new double[segmentLength / 2 + 1];
    }

    public int SegmentLength => _segment;
    public double FrequencyStep => _sampleRate / _segment;

    /// <summary>Calcula el espectro con todos los segmentos que caben en <paramref name="samples"/>; <c>null</c> si no cabe ninguno.</summary>
    public SpectrumResult? Compute(ReadOnlySpan<double> samples)
    {
        if (samples.Length < _segment) return null;

        int hop = _segment / 2;
        int segments = (samples.Length - _segment) / hop + 1;
        // Se usan los segmentos más recientes: el último termina en la última muestra.
        int offset = samples.Length - ((segments - 1) * hop + _segment);
        Array.Clear(_power);

        for (int s = 0; s < segments; s++)
        {
            var segment = samples.Slice(offset + s * hop, _segment);
            double mean = 0;
            foreach (var value in segment) mean += value;
            mean /= _segment;
            for (int i = 0; i < _segment; i++)
            {
                _re[i] = (segment[i] - mean) * _window[i];
                _im[i] = 0;
            }
            Fft.Transform(_re, _im);
            for (int k = 0; k < _power.Length; k++)
                _power[k] += _re[k] * _re[k] + _im[k] * _im[k];
        }

        int bins = _power.Length;
        var amplitude = new double[bins];
        var psd = new double[bins];
        double df = FrequencyStep;
        for (int k = 0; k < bins; k++)
        {
            double mean = _power[k] / segments;
            bool edge = k == 0 || k == bins - 1; // continua y Nyquist no tienen gemelo negativo
            amplitude[k] = (edge ? 1 : 2) * Math.Sqrt(mean) / _windowSum;
            psd[k] = (edge ? 1 : 2) * mean / (_sampleRate * _windowEnergy);
        }

        var bandPower = new double[EegBand.All.Count];
        for (int k = 1; k < bins; k++)
        {
            double f = k * df;
            for (int b = 0; b < EegBand.All.Count; b++)
            {
                if (EegBand.All[b].Contains(f))
                {
                    bandPower[b] += psd[k] * df;
                    break;
                }
            }
        }

        return new SpectrumResult(amplitude, psd, df, bandPower, DominantFrequency(amplitude, df));
    }

    /// <summary>Máximo entre 1 y 45 Hz con interpolación parabólica, que afina la frecuencia por debajo de la resolución del bin.</summary>
    private static double DominantFrequency(double[] amplitude, double df)
    {
        int first = Math.Max(1, (int)Math.Ceiling(DominantLow / df));
        int last = Math.Min(amplitude.Length - 2, (int)Math.Floor(DominantHigh / df));
        int best = -1;
        for (int k = first; k <= last; k++)
        {
            if (best < 0 || amplitude[k] > amplitude[best]) best = k;
        }
        if (best < 0 || amplitude[best] <= 1e-9) return double.NaN;

        double left = amplitude[best - 1], center = amplitude[best], right = amplitude[best + 1];
        double denominator = left - 2 * center + right;
        double shift = Math.Abs(denominator) < 1e-12 ? 0 : Math.Clamp(0.5 * (left - right) / denominator, -0.5, 0.5);
        return (best + shift) * df;
    }
}
