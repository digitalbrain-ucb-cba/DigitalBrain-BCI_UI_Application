using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DigitalBrain.UI.Models;
using DigitalBrain.UI.UseCases.Ports;

namespace DigitalBrain.UI.Services.Backend;

/// <summary>
/// Captura a través del backend: ejecuta <c>python -m digital_brain.cli capture</c>, que lanza el capturador
/// desde su carpeta y guarda la salida a partir de <c>--capture-from</c>. La CLI reenvía por stdout lo que emite
/// el capturador, así que esas líneas alimentan las gráficas en vivo.
/// La CLI numera el archivo como <c>prefijo_N.txt</c>; al terminar se renombra al nombre elegido en la interfaz.
/// </summary>
public sealed partial class PythonCliCaptureGateway(IBackendEnvironment environment) : ICaptureGateway
{
    [GeneratedRegex(@"Datos guardados en:\s*(?<path>.+)$")]
    private static partial Regex SavedPathRegex();

    public async Task<CaptureOutcome> CaptureAsync(
        CaptureRequest request,
        Action<SignalSample> onSample,
        Action<string> onLog,
        CancellationToken cancellationToken)
    {
        var programDirectory = Path.GetDirectoryName(request.ExecutablePath)!;
        var executableName = Path.GetFileName(request.ExecutablePath);
        var outputDirectory = Path.GetDirectoryName(request.OutputPath)!;
        var tempPrefix = $".captura_ui_{Guid.NewGuid():N}";

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
                     "-u", "-m", "digital_brain.cli", "capture", programDirectory, outputDirectory,
                     "--executable-name", executableName,
                     "--prefix", tempPrefix,
                     "--seconds", request.TotalSeconds.ToString(CultureInfo.InvariantCulture),
                     "--capture-from", request.CaptureFromSeconds.ToString(CultureInfo.InvariantCulture),
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
            return CaptureOutcome.Failed($"No se pudo iniciar Python ({environment.PythonExecutable}): {ex.Message}");
        }

        var clock = Stopwatch.StartNew();
        string? savedPath = null;
        var stderr = new StringBuilder();
        var stderrTask = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync() is { } line)
                stderr.AppendLine(line);
        });

        try
        {
            while (await process.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
            {
                if (SignalValidity.TryParseLine(line, clock.Elapsed.TotalSeconds, out var sample))
                {
                    onSample(sample);
                    continue;
                }

                // La CLI imprime «Ejecutando:» justo antes de lanzar el capturador; desde ahí cuentan
                // --seconds y --capture-from, así que el tiempo de las muestras se mide desde esa línea.
                if (line.StartsWith("Ejecutando:", StringComparison.Ordinal))
                    clock.Restart();

                var match = SavedPathRegex().Match(line);
                if (match.Success)
                    savedPath = match.Groups["path"].Value.Trim();
                if (!string.IsNullOrWhiteSpace(line))
                    onLog(line.Trim());
            }
            await process.WaitForExitAsync(cancellationToken);
            await stderrTask;
        }
        catch (OperationCanceledException)
        {
            // La CLI no tiene orden de parada: se termina Python junto con el capturador que lanzó.
            KillTree(process);
            DeleteTemporaryFiles(outputDirectory, tempPrefix);
            return CaptureOutcome.Canceled();
        }

        if (process.ExitCode != 0 || savedPath is null)
        {
            DeleteTemporaryFiles(outputDirectory, tempPrefix);
            var error = stderr.ToString().Trim();
            return CaptureOutcome.Failed(string.IsNullOrEmpty(error)
                ? $"La CLI de captura terminó con código {process.ExitCode}."
                : LastLines(error, 3));
        }

        var producedPath = Path.IsPathRooted(savedPath) ? savedPath : Path.Combine(environment.BackendRoot, savedPath);
        try
        {
            File.Move(producedPath, request.OutputPath, overwrite: false);
        }
        catch (Exception ex)
        {
            return CaptureOutcome.Failed($"La captura quedó en {producedPath} pero no se pudo renombrar: {ex.Message}");
        }
        return CaptureOutcome.Saved(request.OutputPath);
    }

    private static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // El proceso terminó entre la comprobación y Kill.
        }
    }

    private static void DeleteTemporaryFiles(string directory, string prefix)
    {
        foreach (var file in Directory.EnumerateFiles(directory, prefix + "_*.txt"))
        {
            try { File.Delete(file); } catch (IOException) { }
        }
    }

    private static string LastLines(string text, int count) =>
        string.Join(Environment.NewLine, text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).TakeLast(count));
}
