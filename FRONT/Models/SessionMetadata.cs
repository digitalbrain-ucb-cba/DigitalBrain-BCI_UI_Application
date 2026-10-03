namespace DigitalBrain.UI.Models;

/// <summary>
/// Parámetros y metadatos de una sesión de captura de señales.
/// </summary>
public record SessionMetadata(
    string SubjectId,
    string MovementLabel,
    int SessionNumber,
    double DurationSeconds = 30.0,
    double CaptureFromSeconds = 6.0,
    string? Notes = null
)
{
    public string GenerateFileName() =>
        $"{MovementLabel}_{SubjectId}_s{SessionNumber:D2}.txt";
}
