using System.Diagnostics;
using System.Globalization;
using System.IO;
using DigitalBrain.UI.Models;

namespace DigitalBrain.UI.Services.Hardware;

public class ConDumpCaptureService : ICaptureService
{
    private Process? _process;
    private CancellationTokenSource? _cts;
    private bool _isCapturing;

    public bool IsCapturing => _isCapturing;

    public event EventHandler<SignalSample>? SampleReceived;
    public event EventHandler<string>? StatusChanged;
    public event EventHandler<string>? CaptureCompleted;

    public async Task StartCaptureAsync(
        string executablePath,
        string outputDirectory,
        SessionMetadata metadata,
        CancellationToken cancellationToken = default
    )
    {
        if (_isCapturing)
            throw new InvalidOperationException("Ya hay una captura en curso.");

        _isCapturing = true;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        Directory.CreateDirectory(outputDirectory);
        string fileName = metadata.GenerateFileName();
        string destinationFile = Path.Combine(outputDirectory, fileName);

        StatusChanged?.Invoke(this, $"Iniciando captura para sujeto: {metadata.SubjectId}...");

        if (!File.Exists(executablePath))
        {
            // Modo de simulación si no se encuentra el binario físico
            StatusChanged?.Invoke(this, "Ejecutable no encontrado. Iniciando modo SIMULACIÓN a 125 Hz...");
            await RunSimulationAsync(destinationFile, metadata, _cts.Token);
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? "",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            _process = new Process { StartInfo = startInfo };
            _process.Start();

            var startTime = DateTime.UtcNow;
            var deadline = startTime.AddSeconds(metadata.DurationSeconds);
            var captureStart = startTime.AddSeconds(metadata.CaptureFromSeconds);

            using var writer = new StreamWriter(destinationFile, false);

            using var reader = _process.StandardOutput;
            while (!_cts.Token.IsCancellationRequested && DateTime.UtcNow < deadline)
            {
                string? line = await reader.ReadLineAsync(_cts.Token);
                if (line == null) break;

                var now = DateTime.UtcNow;
                var sample = ParseLine(line, (now - startTime).TotalSeconds);

                if (sample != null)
                {
                    SampleReceived?.Invoke(this, sample);
                }

                if (now >= captureStart && now < deadline)
                {
                    await writer.WriteLineAsync(line);
                }
            }

            // Solicitar cierre limpio con 'stop'
            if (!_process.HasExited)
            {
                try
                {
                    await _process.StandardInput.WriteLineAsync("stop");
                    await _process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(2)).Token);
                }
                catch
                {
                    _process.Kill();
                }
            }

            StatusChanged?.Invoke(this, "Captura finalizada con éxito.");
            CaptureCompleted?.Invoke(this, destinationFile);
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Error durante la captura: {ex.Message}");
        }
        finally
        {
            _process?.Dispose();
            _process = null;
            _isCapturing = false;
        }
    }

    public async Task StopCaptureAsync()
    {
        if (_cts != null && !_cts.IsCancellationRequested)
        {
            _cts.Cancel();
        }

        if (_process != null && !_process.HasExited)
        {
            try
            {
                await _process.StandardInput.WriteLineAsync("stop");
            }
            catch
            {
                // Si el proceso ya cerró el stream de entrada
            }
        }
        _isCapturing = false;
        StatusChanged?.Invoke(this, "Captura detenida por el usuario.");
    }

    private static SignalSample? ParseLine(string line, double elapsedSeconds)
    {
        var parts = line.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length < 4) return null;

        if (double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out double ch1) &&
            double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double ch2) &&
            double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out double ch3) &&
            double.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out double ch4))
        {
            bool saturated = SignalSample.CheckSaturation(ch1) ||
                             SignalSample.CheckSaturation(ch2) ||
                             SignalSample.CheckSaturation(ch3) ||
                             SignalSample.CheckSaturation(ch4);

            return new SignalSample(elapsedSeconds, ch1, ch2, ch3, ch4, saturated);
        }
        return null;
    }

    private async Task RunSimulationAsync(string destinationFile, SessionMetadata metadata, CancellationToken token)
    {
        var random = new Random();
        var startTime = DateTime.UtcNow;
        var deadline = startTime.AddSeconds(metadata.DurationSeconds);
        var captureStart = startTime.AddSeconds(metadata.CaptureFromSeconds);

        using var writer = new StreamWriter(destinationFile, false);
        double phase = 0;

        while (!token.IsCancellationRequested && DateTime.UtcNow < deadline)
        {
            var now = DateTime.UtcNow;
            double elapsed = (now - startTime).TotalSeconds;

            phase += 0.1;
            // Señales simuladas de 4 canales (ondas compuestas + ruido)
            double ch1 = 40.0 * Math.Sin(phase * 1.2) + (random.NextDouble() * 10.0 - 5.0);
            double ch2 = 30.0 * Math.Cos(phase * 0.8) + (random.NextDouble() * 10.0 - 5.0);
            double ch3 = 25.0 * Math.Sin(phase * 2.5) + (random.NextDouble() * 8.0 - 4.0);
            double ch4 = 35.0 * Math.Cos(phase * 1.7) + (random.NextDouble() * 12.0 - 6.0);

            var sample = new SignalSample(elapsed, ch1, ch2, ch3, ch4, false);
            SampleReceived?.Invoke(this, sample);

            if (now >= captureStart && now < deadline)
            {
                await writer.WriteLineAsync(
                    $"{ch1.ToString("F2", CultureInfo.InvariantCulture)},{ch2.ToString("F2", CultureInfo.InvariantCulture)},{ch3.ToString("F2", CultureInfo.InvariantCulture)},{ch4.ToString("F2", CultureInfo.InvariantCulture)}"
                );
            }

            // Simular 125 Hz (8 ms por muestra)
            await Task.Delay(8, token);
        }

        StatusChanged?.Invoke(this, "Simulación finalizada.");
        CaptureCompleted?.Invoke(this, destinationFile);
        _isCapturing = false;
    }
}
