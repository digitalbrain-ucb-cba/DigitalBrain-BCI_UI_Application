using System.Globalization;

namespace DigitalBrain.UI.Models;

/// <summary>
/// Reglas de validez de una línea capturada, equivalentes a <c>parse_sample</c> del backend:
/// la línea necesita cuatro valores numéricos separados por comas y se descarta si alguno vale ±800 µV.
/// </summary>
public static class SignalValidity
{
    public static bool IsSaturated(double value) =>
        value == AcquisitionParameters.SaturationMicroVolts || value == -AcquisitionParameters.SaturationMicroVolts;

    public static bool TryParseLine(string? line, double elapsedSeconds, out SignalSample sample)
    {
        sample = default!;
        if (string.IsNullOrWhiteSpace(line)) return false;

        var parts = line.Trim().Split(',');
        if (parts.Length < 4) return false;

        var values = new double[4];
        for (int i = 0; i < 4; i++)
        {
            if (!double.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
                return false;
        }

        bool saturated = values.Any(IsSaturated);
        sample = new SignalSample(elapsedSeconds, values[0], values[1], values[2], values[3], saturated);
        return true;
    }
}
