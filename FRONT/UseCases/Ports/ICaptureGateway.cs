using DigitalBrain.UI.Models;

namespace DigitalBrain.UI.UseCases.Ports;

/// <summary>
/// Puerto hacia el backend que ejecuta el capturador y guarda la sesión en disco.
/// </summary>
public interface ICaptureGateway
{
    /// <param name="onSample">Se invoca desde un hilo de fondo por cada línea de señal recibida.</param>
    /// <param name="onLog">Se invoca desde un hilo de fondo con las líneas de texto del backend.</param>
    Task<CaptureOutcome> CaptureAsync(
        CaptureRequest request,
        Action<SignalSample> onSample,
        Action<string> onLog,
        CancellationToken cancellationToken);
}

/// <param name="ExecutablePath">Ruta completa del ejecutable (p. ej. ConDumpAuto.exe).</param>
/// <param name="OutputPath">Archivo final que debe quedar guardado.</param>
public sealed record CaptureRequest(
    string ExecutablePath,
    string OutputPath,
    double TotalSeconds,
    double CaptureFromSeconds);

public sealed record CaptureOutcome(bool Succeeded, bool Cancelled, string? SavedPath, string? Error)
{
    public static CaptureOutcome Saved(string path) => new(true, false, path, null);
    public static CaptureOutcome Canceled() => new(false, true, null, null);
    public static CaptureOutcome Failed(string error) => new(false, false, null, error);
}
