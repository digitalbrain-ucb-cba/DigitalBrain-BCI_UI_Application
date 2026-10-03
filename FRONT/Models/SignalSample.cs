namespace DigitalBrain.UI.Models;

/// <summary>
/// Representa una muestra individual de señal adquirida con 4 canales analógicos a 125 Hz.
/// </summary>
public record SignalSample(
    double TimeSeconds,
    double Channel1,
    double Channel2,
    double Channel3,
    double Channel4,
    bool IsSaturated = false
)
{
    public const double SaturationPositive = 800.0;
    public const double SaturationNegative = -800.0;

    public static bool CheckSaturation(double val) =>
        Math.Abs(val) >= 799.0;
}
