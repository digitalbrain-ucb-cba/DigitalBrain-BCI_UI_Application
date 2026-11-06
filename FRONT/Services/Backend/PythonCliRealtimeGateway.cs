using System.Diagnostics;
using System.Text;
using DigitalBrain.UI.UseCases.Ports;

namespace DigitalBrain.UI.Services.Backend;

/// <summary>
/// Predicción a través del backend: ejecuta <c>python -m digital_brain.cli predict</c>, que lanza el capturador
/// y escribe una línea <c>Predicción: etiqueta</c> por ventana. No termina sola: al cancelar se cierra Python
/// junto con el capturador.
/// </summary>
public sealed class PythonCliRealtimeGateway(IBackendEnvironment environment) : IRealtimeGateway
{
    private const string PredictionPrefix = "Predicción:";

    public async Task<RealtimeOutcome> RunAsync(
        RealtimeRequest request,
        Action<string> onPrediction,
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

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return RealtimeOutcome.Failed($"No se pudo iniciar Python ({environment.PythonExecutable}): {ex.Message}");
        }

        // Cancel() mata el árbol en el mismo hilo: al cerrar la aplicación no queda tiempo para una continuación asíncrona.
        using var registration = cancellationToken.Register(() => PythonCliCaptureGateway.KillTree(process));

        var stderr = new StringBuilder();
        var stderrTask = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync() is { } line)
                stderr.AppendLine(line);
        });

        while (await process.StandardOutput.ReadLineAsync() is { } line)
        {
            if (line.StartsWith(PredictionPrefix, StringComparison.Ordinal))
                onPrediction(line[PredictionPrefix.Length..].Trim());
            else if (!string.IsNullOrWhiteSpace(line))
                onLog(line.Trim());
        }
        await process.WaitForExitAsync();
        await stderrTask;

        if (cancellationToken.IsCancellationRequested)
            return RealtimeOutcome.Canceled();

        var error = stderr.ToString().Trim();
        return RealtimeOutcome.Failed(string.IsNullOrEmpty(error)
            ? $"La predicción terminó con código {process.ExitCode}."
            : PythonCliCaptureGateway.LastLines(error, 3));
    }
}
