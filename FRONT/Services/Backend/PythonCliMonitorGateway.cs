using System.Diagnostics;
using System.Text;
using DigitalBrain.UI.Models;
using DigitalBrain.UI.UseCases.Ports;

namespace DigitalBrain.UI.Services.Backend;

/// <summary>
/// Señal en vivo a través del backend: ejecuta <c>python -m digital_brain.cli monitor</c>, que lanza el capturador y reenvía
/// cada muestra como <c>a,b,c,d</c> (también las saturadas) sin guardar nada. Igual que <c>predict</c>, no termina sola: al cancelar
/// se le pide parar por stdin para que el capturador libere el casco, y pasado <see cref="StopTimeout"/> se cierra a la fuerza.
/// </summary>
public sealed class PythonCliMonitorGateway(IBackendEnvironment environment) : ISignalMonitorGateway
{
    /// <summary>Python da 3 s al capturador para cerrar y 1 s más para terminarlo.</summary>
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(6);

    public async Task<SignalSourceOutcome> RunAsync(
        string executablePath,
        Action<SignalSample> onSample,
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
                     "-u", "-m", "digital_brain.cli", "monitor",
                     Path.GetDirectoryName(executablePath)!,
                     "--executable-name", Path.GetFileName(executablePath),
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
            return SignalSourceOutcome.Failed($"No se pudo iniciar Python ({environment.PythonExecutable}): {ex.Message}");
        }

        using var registration = cancellationToken.Register(() => RequestStop(process));

        var clock = Stopwatch.StartNew();
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
                if (SignalValidity.TryParseLine(line, clock.Elapsed.TotalSeconds, out var sample))
                    onSample(sample);
                else if (!string.IsNullOrWhiteSpace(line))
                    onLog(line.Trim());
            }
        });

        try
        {
            await outputTask.WaitAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            await stderrTask.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await WaitForStopAsync(process);
            return SignalSourceOutcome.Canceled();
        }

        var exitCode = process.ExitCode;
        process.Dispose();
        var error = stderr.ToString().Trim();
        return SignalSourceOutcome.Failed(string.IsNullOrEmpty(error)
            ? $"El monitor terminó con código {exitCode}."
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
}
