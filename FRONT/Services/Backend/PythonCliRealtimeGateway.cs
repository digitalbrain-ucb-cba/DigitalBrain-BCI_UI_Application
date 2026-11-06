using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DigitalBrain.UI.UseCases.Ports;

namespace DigitalBrain.UI.Services.Backend;

/// <summary>
/// Predicción a través del backend: ejecuta <c>python -m digital_brain.cli predict</c>, que lanza el capturador
/// y escribe una línea <c>Predicción: etiqueta | confianza: 87.0%</c> por ventana. No termina sola: al cancelar se cierra Python
/// junto con el capturador.
/// </summary>
public sealed partial class PythonCliRealtimeGateway(IBackendEnvironment environment) : IRealtimeGateway
{
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
            if (PredictionRegex().Match(line) is { Success: true } match)
                onPrediction(ToPrediction(match));
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

    private static RealtimePrediction ToPrediction(Match match)
    {
        var confidence = match.Groups["confidence"];
        return new RealtimePrediction(
            match.Groups["label"].Value,
            confidence.Success ? double.Parse(confidence.Value, CultureInfo.InvariantCulture) / 100 : null);
    }
}
