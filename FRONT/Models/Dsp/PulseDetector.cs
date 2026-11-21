namespace DigitalBrain.UI.Models.Dsp;

/// <summary>Pulso detectado en un canal.</summary>
/// <param name="SampleIndex">Índice absoluto de la muestra en que la señal cruzó el umbral.</param>
/// <param name="Peak">Valor (µV, con signo) más alejado de la mediana mientras duró el pulso.</param>
public sealed record PulseEvent(int Channel, long SampleIndex, double Peak);

/// <summary>
/// Detecta pulsos (parpadeos, contracciones, golpes en el electrodo, espigas) en un canal ya filtrado, con un umbral que se adapta al
/// ruido: mediana ± k·σ, donde σ = 1,4826·MAD (desviación absoluta mediana) de los últimos segundos. A diferencia de la desviación
/// típica, la MAD casi no cambia con los propios pulsos, así que un pulso grande no sube el umbral y oculta los siguientes.
/// Tras un pulso hay un periodo refractario y la señal debe volver por debajo de medio umbral antes de detectar otro.
/// </summary>
public sealed class PulseDetector
{
    /// <summary>Umbral mínimo en µV: con una señal plana (casco desconectado, archivo de ceros) la MAD vale 0.</summary>
    public const double MinimumThreshold = 5;

    private readonly int _channel;
    private readonly double[] _history;
    private readonly double[] _scratch;
    private readonly int _refractory;
    private int _count, _head;
    private long _lastPulse = long.MinValue / 2;
    private bool _inPulse;
    private PulseEvent? _current;

    /// <param name="historySamples">Muestras con que se estima el ruido (unos 2 s).</param>
    /// <param name="refractorySamples">Muestras mínimas entre dos pulsos.</param>
    public PulseDetector(int channel, int historySamples, int refractorySamples)
    {
        _channel = channel;
        _history = new double[Math.Max(16, historySamples)];
        _scratch = new double[_history.Length];
        _refractory = Math.Max(1, refractorySamples);
    }

    /// <summary>Número de desviaciones (k) a partir del cual una muestra es un pulso.</summary>
    public double Sensitivity { get; set; } = 5;

    public double Median { get; private set; }
    /// <summary>Distancia a la mediana (µV) que dispara un pulso; NaN mientras no hay suficiente historia.</summary>
    public double Threshold { get; private set; } = double.NaN;
    public bool IsReady => _count >= _history.Length / 2;

    /// <summary>Recalcula mediana y umbral con la historia actual; se llama una vez por bloque de muestras.</summary>
    public void UpdateThreshold()
    {
        if (!IsReady)
        {
            Threshold = double.NaN;
            return;
        }
        var span = _scratch.AsSpan(0, _count);
        _history.AsSpan(0, _count).CopyTo(span);
        span.Sort();
        Median = MedianOfSorted(span);
        for (int i = 0; i < span.Length; i++) span[i] = Math.Abs(span[i] - Median);
        span.Sort();
        double sigma = 1.4826 * MedianOfSorted(span);
        Threshold = Math.Max(Sensitivity * sigma, MinimumThreshold);
    }

    /// <summary>Procesa una muestra; devuelve el pulso cuando la señal cruza el umbral.</summary>
    /// <param name="saturated">Las muestras saturadas no cuentan para el ruido ni disparan pulsos: se indican aparte.</param>
    public PulseEvent? Process(long index, double value, bool saturated)
    {
        if (saturated) return null;

        _history[_head] = value;
        _head = (_head + 1) % _history.Length;
        if (_count < _history.Length) _count++;
        if (double.IsNaN(Threshold)) return null;

        double deviation = value - Median;
        if (_inPulse)
        {
            if (Math.Abs(deviation) > Math.Abs(_current!.Peak - Median))
                _current = _current with { Peak = value };
            if (Math.Abs(deviation) < Threshold / 2)
                _inPulse = false;
            return null;
        }

        if (Math.Abs(deviation) >= Threshold && index - _lastPulse >= _refractory)
        {
            _inPulse = true;
            _lastPulse = index;
            _current = new PulseEvent(_channel, index, value);
            return _current;
        }
        return null;
    }

    /// <summary>Pico del pulso más reciente, que sigue creciendo mientras la señal no vuelve por debajo de medio umbral.</summary>
    public PulseEvent? LastPulse => _current;

    private static double MedianOfSorted(Span<double> sorted)
    {
        int n = sorted.Length;
        return n % 2 == 1 ? sorted[n / 2] : 0.5 * (sorted[n / 2 - 1] + sorted[n / 2]);
    }
}
