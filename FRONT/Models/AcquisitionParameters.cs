namespace DigitalBrain.UI.Models;

/// <summary>
/// Parámetros fijos de adquisición y ventaneo. Reflejan los valores que usa el backend
/// (<c>SpectralFeatureExtractor</c> y <c>build_dataset</c>) para que la interfaz muestre lo que realmente se entrena.
/// </summary>
public static class AcquisitionParameters
{
    public const double SampleRateHz = 125.0;
    public const int WindowSize = 125;
    /// <summary>Avance entre ventanas: <c>window_size // 2</c> en el backend.</summary>
    public const int WindowStep = WindowSize / 2;
    public const double SaturationMicroVolts = 800.0;
    /// <summary>Segundos iniciales de estabilización que el capturador corre sin guardar, aparte de la duración.</summary>
    public const double DefaultCaptureFromSeconds = 5.0;
    public static readonly IReadOnlyList<string> ChannelNames = ["A", "B", "C", "D"];

    public static double OverlapRatio => 1.0 - (double)WindowStep / WindowSize;

    /// <summary>Número de ventanas que <c>build_dataset</c> genera a partir de las muestras válidas.</summary>
    public static int WindowCount(long validSamples) =>
        validSamples < WindowSize ? 0 : (int)((validSamples - WindowSize) / WindowStep) + 1;
}
