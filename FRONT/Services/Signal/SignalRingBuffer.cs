using DigitalBrain.UI.Models;

namespace DigitalBrain.UI.Services.Signal;

/// <summary>
/// Búfer circular concurrente de alto rendimiento para almacenar y alimentar
/// el osciloscopio a 125 Hz sin bloqueos ni asignaciones de memoria excesivas.
/// </summary>
public class SignalRingBuffer
{
    private readonly SignalSample[] _buffer;
    private readonly object _lock = new();
    private int _head = 0;
    private int _count = 0;

    public int Capacity { get; }

    public SignalRingBuffer(int capacity = 1250) // Por defecto: 10 segundos a 125 Hz
    {
        Capacity = capacity;
        _buffer = new SignalSample[capacity];
    }

    public void Add(SignalSample sample)
    {
        lock (_lock)
        {
            _buffer[_head] = sample;
            _head = (_head + 1) % Capacity;
            if (_count < Capacity)
                _count++;
        }
    }

    public void AddRange(IEnumerable<SignalSample> samples)
    {
        lock (_lock)
        {
            foreach (var sample in samples)
            {
                _buffer[_head] = sample;
                _head = (_head + 1) % Capacity;
                if (_count < Capacity)
                    _count++;
            }
        }
    }

    /// <summary>
    /// Extrae una copia ordenada cronológicamente de las muestras actuales para graficar.
    /// </summary>
    public SignalSample[] ToArray()
    {
        lock (_lock)
        {
            var result = new SignalSample[_count];
            if (_count == 0) return result;

            int start = (_head - _count + Capacity) % Capacity;
            for (int i = 0; i < _count; i++)
            {
                result[i] = _buffer[(start + i) % Capacity];
            }
            return result;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _head = 0;
            _count = 0;
        }
    }
}
