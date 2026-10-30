namespace DigitalBrain.UI.Models;

/// <summary>
/// Parámetros dinámicos de adquisición y ventaneo.
/// Cuando se modifica la frecuencia de muestreo (<see cref="SampleRateHz"/>), se actualizan los tamaños de ventana y cálculos en todo el sistema.
/// </summary>
public static class AcquisitionParameters
{
    public const double DefaultSampleRateHz = 125.0;
    public const double SaturationMicroVolts = 800.0;
    /// <summary>Segundos iniciales de estabilización que el capturador corre sin guardar, aparte de la duración.</summary>
    public const double DefaultCaptureFromSeconds = 5.0;
    public static readonly IReadOnlyList<string> ChannelNames = ["A", "B", "C", "D"];

    private static double _sampleRateHz = DefaultSampleRateHz;

    /// <summary>Evento emitido cuando la frecuencia de muestreo cambia en el sistema.</summary>
    public static event Action? SampleRateChanged;

    public static double SampleRateHz
    {
        get => _sampleRateHz;
        set
        {
            if (value <= 0 || Math.Abs(_sampleRateHz - value) < 0.001) return;
            _sampleRateHz = value;
            SampleRateChanged?.Invoke();
        }
    }

    /// <summary>Tamaño de ventana en muestras (1 segundo de señal: redondeado a entero).</summary>
    public static int WindowSize => Math.Max(1, (int)Math.Round(SampleRateHz));

    /// <summary>Avance entre ventanas: <c>window_size // 2</c> en el backend (solapamiento al 50%).</summary>
    public static int WindowStep => Math.Max(1, WindowSize / 2);

    public static double OverlapRatio => 1.0 - (double)WindowStep / WindowSize;

    /// <summary>Número de ventanas que <c>build_dataset</c> genera a partir de las muestras válidas.</summary>
    public static int WindowCount(long validSamples) =>
        validSamples < WindowSize ? 0 : (int)((validSamples - WindowSize) / WindowStep) + 1;
}
