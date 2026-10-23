namespace DigitalBrain.UI.Models;

/// <summary>
/// Lo que el usuario configuró para una grabación, ya con las rutas resueltas.
/// <see cref="Validate"/> reúne las reglas que deben cumplirse antes de lanzar la captura.
/// </summary>
/// <param name="DurationSeconds">Segundos que se guardan en el archivo.</param>
/// <param name="CaptureFromSeconds">Segundos iniciales de estabilización que el capturador no guarda; se suman aparte.</param>
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
    /// <summary>Tiempo que corre el capturador (<c>--seconds</c>): estabilización más duración guardada.</summary>
    public double TotalSeconds => CaptureFromSeconds + DurationSeconds;
    public string FileName => Movement.FileNameFor(SessionNumber);
    public string OutputPath => Path.Combine(OutputDirectory, FileName);

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(SubjectId))
            errors.Add("Elige o añade el sujeto.");
        else if (CatalogNames.Validate(SubjectId, "sujeto") is { } subjectError)
            errors.Add(subjectError);
        if (SessionNumber <= 0)
            errors.Add("El número de sesión debe ser mayor que cero.");
        if (DurationSeconds <= 0)
            errors.Add("La duración debe ser mayor que cero.");
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
