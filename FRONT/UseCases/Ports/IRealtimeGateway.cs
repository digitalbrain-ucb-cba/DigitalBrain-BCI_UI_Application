namespace DigitalBrain.UI.UseCases.Ports;

/// <summary>
/// Puerto hacia el backend que ejecuta el capturador y clasifica su salida hasta que se cancela.
/// </summary>
public interface IRealtimeGateway
{
    /// <param name="onPrediction">Se invoca desde un hilo de fondo con la etiqueta de cada ventana.</param>
    /// <param name="onLog">Se invoca desde un hilo de fondo con las líneas de estado del capturador.</param>
    Task<RealtimeOutcome> RunAsync(
        RealtimeRequest request,
        Action<string> onPrediction,
        Action<string> onLog,
        CancellationToken cancellationToken);
}

/// <param name="ExecutablePath">Ruta completa del ejecutable (p. ej. ConDumpAuto.exe).</param>
/// <param name="ModelPath">Modelo <c>.pkl</c> entrenado en la aplicación o con MachineLearning.py.</param>
public sealed record RealtimeRequest(string ExecutablePath, string ModelPath);

public sealed record RealtimeOutcome(bool Cancelled, string? Error)
{
    public static RealtimeOutcome Canceled() => new(true, null);
    public static RealtimeOutcome Failed(string error) => new(false, error);
}
