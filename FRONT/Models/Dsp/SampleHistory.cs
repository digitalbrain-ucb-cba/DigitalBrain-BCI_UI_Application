namespace DigitalBrain.UI.Models.Dsp;

/// <summary>
/// Últimas muestras de los cuatro canales, direccionadas por su índice absoluto (0 = primera muestra recibida).
/// Guarda la señal sin procesar, la filtrada y si la muestra estaba saturada. No es segura entre hilos: la usa solo el hilo de la interfaz.
/// </summary>
public sealed class SampleHistory
{
    public const int Channels = 4;

    private readonly double[][] _raw;
    private readonly double[][] _filtered;
    private readonly bool[] _saturated;

    public SampleHistory(int capacity)
    {
        Capacity = Math.Max(1, capacity);
        _raw = Enumerable.Range(0, Channels).Select(_ => new double[Capacity]).ToArray();
        _filtered = Enumerable.Range(0, Channels).Select(_ => new double[Capacity]).ToArray();
        _saturated = new bool[Capacity];
    }

    public int Capacity { get; }
    /// <summary>Muestras recibidas desde el último <see cref="Clear"/>; el índice de la siguiente.</summary>
    public long Total { get; private set; }
    /// <summary>Índice de la muestra más antigua que se conserva.</summary>
    public long Oldest => Math.Max(0, Total - Capacity);

    public bool Contains(long index) => index >= Oldest && index < Total;

    public long Append(ReadOnlySpan<double> raw, ReadOnlySpan<double> filtered, bool saturated)
    {
        int slot = (int)(Total % Capacity);
        for (int c = 0; c < Channels; c++)
        {
            _raw[c][slot] = raw[c];
            _filtered[c][slot] = filtered[c];
        }
        _saturated[slot] = saturated;
        return Total++;
    }

    public double Raw(int channel, long index) => _raw[channel][Slot(index)];
    public double Filtered(int channel, long index) => _filtered[channel][Slot(index)];
    public bool IsSaturated(long index) => _saturated[Slot(index)];

    public void SetFiltered(int channel, long index, double value) => _filtered[channel][Slot(index)] = value;

    /// <summary>Copia en <paramref name="destination"/> la señal filtrada de las últimas <c>destination.Length</c> muestras (o menos).</summary>
    public int CopyLatestFiltered(int channel, Span<double> destination)
    {
        long count = Math.Min(destination.Length, Total - Oldest);
        long start = Total - count;
        for (long i = 0; i < count; i++)
            destination[(int)i] = _filtered[channel][Slot(start + i)];
        return (int)count;
    }

    public void Clear() => Total = 0;

    private int Slot(long index) => (int)(index % Capacity);
}
