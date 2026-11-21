using DigitalBrain.UI.Models;

namespace DigitalBrain.UI.UseCases.Ports;

/// <summary>
/// Puerto hacia el backend que transmite en vivo la señal del casco, sin guardarla, hasta que se cancela.
/// Entrega también las muestras saturadas (marcadas en <see cref="SignalSample.IsSaturated"/>).
/// </summary>
public interface ISignalMonitorGateway
{
    /// <param name="onSample">Se invoca desde un hilo de fondo por cada muestra.</param>
    /// <param name="onLog">Se invoca desde un hilo de fondo con las líneas de estado del capturador.</param>
    Task<SignalSourceOutcome> RunAsync(
        string executablePath,
        Action<SignalSample> onSample,
        Action<string> onLog,
        CancellationToken cancellationToken);
}

/// <summary>Reproduce una grabación <c>.txt</c> a la velocidad de muestreo, como si llegara del casco.</summary>
public interface IRecordingPlayback
{
    /// <param name="loop">Al llegar al final vuelve a empezar hasta que se cancela.</param>
    /// <param name="onSample">Se invoca desde un hilo de fondo por cada muestra.</param>
    Task<SignalSourceOutcome> PlayAsync(
        string recordingPath,
        double sampleRateHz,
        bool loop,
        Action<SignalSample> onSample,
        CancellationToken cancellationToken);
}

public sealed record SignalSourceOutcome(bool Cancelled, string? Error)
{
    public static SignalSourceOutcome Canceled() => new(true, null);
    public static SignalSourceOutcome Finished() => new(false, null);
    public static SignalSourceOutcome Failed(string error) => new(false, error);
}
