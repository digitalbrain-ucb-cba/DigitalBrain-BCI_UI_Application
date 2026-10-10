namespace DigitalBrain.UI.Models;

/// <summary>
/// Lo que el usuario configuró para una grabación, ya con las rutas resueltas.
/// <see cref="Validate"/> reúne las reglas que deben cumplirse antes de lanzar la captura.
/// </summary>
public sealed record RecordingPlan(
    string SubjectId,
    MovementClass Movement,
    int SessionNumber,
    double DurationSeconds,
    string? Notes,
    string ExecutablePath,
    string OutputDirectory,
    double CaptureFromSeconds = AcquisitionParameters.DefaultCaptureFromSeconds)
{
    public string FileName => Movement.FileNameFor(SessionNumber);
    public string OutputPath => Path.Combine(OutputDirectory, FileName);

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(SubjectId))
            errors.Add("Indica el sujeto.");
        if (SessionNumber <= 0)
            errors.Add("El número de sesión debe ser mayor que cero.");
        if (DurationSeconds <= CaptureFromSeconds)
            errors.Add($"La duración debe superar los {CaptureFromSeconds:0} s iniciales que el capturador descarta.");
        if (string.IsNullOrWhiteSpace(ExecutablePath) || !File.Exists(ExecutablePath))
            errors.Add("No se encontró el ejecutable del capturador; elige la ruta completa a ConDumpAuto.exe.");
        if (string.IsNullOrWhiteSpace(OutputDirectory))
            errors.Add("Elige la carpeta de salida.");
        else if (File.Exists(OutputPath))
            errors.Add($"Ya existe {FileName}; cambia el número de sesión.");
        return errors;
    }

    public SessionMetadata ToMetadata() =>
        new(SubjectId, Movement.Label, SessionNumber, DurationSeconds, CaptureFromSeconds, Notes);
}
