using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DigitalBrain.UI.UseCases.Ports;

namespace DigitalBrain.UI.Services.Backend;

/// <summary>
/// Predicción a través del backend: ejecuta <c>python -m digital_brain.cli predict</c>, que lanza el capturador
/// y escribe una línea <c>Predicción: etiqueta | confianza: 87.0%</c> por ventana. No termina sola: al cancelar se le
/// pide parar por stdin para que el capturador libere el casco, y pasado <see cref="StopTimeout"/> se cierra a la fuerza.
/// </summary>
public sealed partial class PythonCliRealtimeGateway(IBackendEnvironment environment) : IRealtimeGateway
{
    /// <summary>Python da 3 s al capturador para cerrar y 1 s más para terminarlo.</summary>
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(6);

    [GeneratedRegex(@"^Predicción:\s*(?<label>.+?)(?:\s+\|\s+confianza:\s*(?<confidence>[0-9.]+)%)?\s*$")]
    private static partial Regex PredictionRegex();

    public async Task<RealtimeOutcome> RunAsync(
        RealtimeRequest request,
        Action<RealtimePrediction> onPrediction,
        Action<string> onLog,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = environment.PythonExecutable,
            WorkingDirectory = environment.BackendRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        foreach (var argument in new[]
                 {
                     "-u", "-m", "digital_brain.cli", "predict",
                     Path.GetDirectoryName(request.ExecutablePath)!, request.ModelPath,
                     "--executable-name", Path.GetFileName(request.ExecutablePath),
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        // Sin using: al cancelar puede quedar una lectura bloqueada en una tubería que otro proceso mantiene abierta.
        var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            process.Dispose();
            return RealtimeOutcome.Failed($"No se pudo iniciar Python ({environment.PythonExecutable}): {ex.Message}");
        }

        // Escribe en el mismo hilo de Cancel(): al cerrar la aplicación no queda tiempo para una continuación asíncrona.
        using var registration = cancellationToken.Register(() => RequestStop(process));

        var stderr = new StringBuilder();
        var stderrTask = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync() is { } line)
                stderr.AppendLine(line);
        });
        var outputTask = Task.Run(async () =>
        {
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                if (cancellationToken.IsCancellationRequested)
                    continue;
                if (PredictionRegex().Match(line) is { Success: true } match)
                    onPrediction(ToPrediction(match));
                else if (!string.IsNullOrWhiteSpace(line))
                    onLog(line.Trim());
            }
        });

        try
        {
            // Las esperas se abandonan al cancelar: nunca dependen de que se cierren las tuberías.
            await outputTask.WaitAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            await stderrTask.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await WaitForStopAsync(process);
            return RealtimeOutcome.Canceled();
        }

        var exitCode = process.ExitCode;
        process.Dispose();
        var error = stderr.ToString().Trim();
        return RealtimeOutcome.Failed(string.IsNullOrEmpty(error)
            ? $"La predicción terminó con código {exitCode}."
            : PythonCliCaptureGateway.LastLines(error, 3));
    }

    private static void RequestStop(Process process)
    {
        try
        {
            process.StandardInput.WriteLine("stop");
            process.StandardInput.Flush();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
        {
            // Python ya terminó.
        }
    }

    private static async Task WaitForStopAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync().WaitAsync(StopTimeout);
        }
        catch (TimeoutException)
        {
            PythonCliCaptureGateway.KillTree(process);
        }
    }

    private static RealtimePrediction ToPrediction(Match match)
    {
        var confidence = match.Groups["confidence"];
        return new RealtimePrediction(
            match.Groups["label"].Value,
            confidence.Success ? double.Parse(confidence.Value, CultureInfo.InvariantCulture) / 100 : null);
    }
}
