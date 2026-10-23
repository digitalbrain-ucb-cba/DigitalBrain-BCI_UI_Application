using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using DigitalBrain.UI.Models;

namespace DigitalBrain.UI.Services.Backend;

public class PythonCliBridge : IBackendBridge
{
    private readonly string _pythonExecutable;
    private readonly string _backendRootPath;

    public PythonCliBridge(string? pythonExecutable = null, string? backendRootPath = null)
    {
        _pythonExecutable = pythonExecutable ?? "python";
        
        // Localizar carpeta BACK por defecto
        _backendRootPath = backendRootPath ?? ResolveBackendPath();
    }

    public string? LastError { get; private set; }

    public async Task<bool> ValidatePythonEnvironmentAsync()
    {
        try
        {
            var result = await RunProcessAsync(_pythonExecutable, "--version", _backendRootPath);
            return result.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> BuildDatasetAsync(
        string inputDirectory,
        string outputCsvPath,
        string label,
        IProgress<string>? progress = null
    )
    {
        LastError = null;
        progress?.Report($"Construyendo dataset para etiqueta '{label}'...");

        string args = $"-m digital_brain.cli build-dataset \"{inputDirectory}\" \"{outputCsvPath}\" \"{label}\"";
        var result = await RunProcessAsync(_pythonExecutable, args, _backendRootPath, progress);

        if (result.ExitCode == 0)
        {
            progress?.Report($"Dataset generado exitosamente en: {outputCsvPath}");
            return true;
        }

        LastError = LastErrorLine(result.StandardError);
        progress?.Report($"Error al construir dataset: {result.StandardError}");
        return false;
    }

    public async Task<TrainingResult?> TrainModelAsync(
        string outputModelPath,
        IEnumerable<string> datasetPaths,
        string classifierType = "random_forest",
        IProgress<string>? progress = null
    )
    {
        LastError = null;
        progress?.Report($"Iniciando entrenamiento ({classifierType})...");

        string datasetsArg = string.Join(" ", datasetPaths.Select(d => $"\"{d}\""));
        string args = $"-m digital_brain.cli train \"{outputModelPath}\" {datasetsArg} --algorithm {classifierType}";

        var result = await RunProcessAsync(_pythonExecutable, args, _backendRootPath, progress);

        if (result.ExitCode == 0)
        {
            double accuracy = 0.0;
            // Buscar "exactitud: X.XXX" en la salida
            var match = Regex.Match(result.StandardOutput, @"exactitud:\s*([0-9]*\.?[0-9]+)");
            if (match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedAcc))
            {
                accuracy = parsedAcc;
            }

            progress?.Report($"Modelo entrenado exitosamente con exactitud: {accuracy:P1}");
            return new TrainingResult(outputModelPath, accuracy, classifierType, DateTime.Now);
        }

        LastError = LastErrorLine(result.StandardError);
        progress?.Report($"Error en entrenamiento: {result.StandardError}");
        return null;
    }

    /// <summary>La última línea de un traceback es la excepción; se quita el tipo si es un error de validación.</summary>
    private static string? LastErrorLine(string standardError)
    {
        var line = standardError.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0);
        return line is null ? null : Regex.Replace(line, @"^(ValueError|FileNotFoundError):\s*", "");
    }

    private static string ResolveBackendPath()
    {
        // Si ejecutamos desde bin/Debug/..., subir hasta la raíz del repo
        string current = AppDomain.CurrentDomain.BaseDirectory;
        var dir = new DirectoryInfo(current);

        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "BACK");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunProcessAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        IProgress<string>? progress = null
    )
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        var outputBuilder = new System.Text.StringBuilder();
        var errorBuilder = new System.Text.StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                outputBuilder.AppendLine(e.Data);
                progress?.Report(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                errorBuilder.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();
        return (process.ExitCode, outputBuilder.ToString(), errorBuilder.ToString());
    }
}
