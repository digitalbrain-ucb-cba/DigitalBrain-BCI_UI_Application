namespace DigitalBrain.UI.Models;

/// <summary>Resumen de un archivo de captura guardado.</summary>
public sealed record RecordingSummary(
    string FileName,
    string FullPath,
    MovementClass? Movement,
    string? Subject,
    double DurationSeconds,
    long ValidSamples,
    long DiscardedSamples,
    DateTime LastWriteTime)
{
    public int Windows => AcquisitionParameters.WindowCount(ValidSamples);
}
