using System.Diagnostics;
using DigitalBrain.UI.Models;
using DigitalBrain.UI.UseCases.Ports;

namespace DigitalBrain.UI.Services.Signal;

/// <summary>
/// Reproduce una captura <c>.txt</c> (cabeceras de ConDump incluidas) a la frecuencia de muestreo, con el reloj del sistema:
/// cada pocos milisegundos entrega las muestras que tocan desde el inicio, así que el ritmo medio es exacto aunque el temporizador no lo sea.
/// </summary>
public sealed class FileRecordingPlayback : IRecordingPlayback
{
    public async Task<SignalSourceOutcome> PlayAsync(
        string recordingPath,
        double sampleRateHz,
        bool loop,
        Action<SignalSample> onSample,
        CancellationToken cancellationToken)
    {
        SignalSample[] samples;
        try
        {
            samples = await Task.Run(() => File.ReadLines(recordingPath)
                .Select(line => SignalValidity.TryParseLine(line, 0, out var sample) ? sample : null)
                .OfType<SignalSample>()
                .ToArray(), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return SignalSourceOutcome.Canceled();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return SignalSourceOutcome.Failed($"No se pudo leer {Path.GetFileName(recordingPath)}: {ex.Message}");
        }

        if (samples.Length == 0)
            return SignalSourceOutcome.Failed($"{Path.GetFileName(recordingPath)} no tiene muestras de cuatro canales.");

        var clock = Stopwatch.StartNew();
        long emitted = 0;
        try
        {
            while (true)
            {
                long due = (long)(clock.Elapsed.TotalSeconds * sampleRateHz);
                for (; emitted < due; emitted++)
                {
                    if (!loop && emitted >= samples.Length)
                        return SignalSourceOutcome.Finished();
                    var sample = samples[emitted % samples.Length];
                    onSample(sample with { TimeSeconds = emitted / sampleRateHz });
                }
                await Task.Delay(10, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            return SignalSourceOutcome.Canceled();
        }
    }
}
