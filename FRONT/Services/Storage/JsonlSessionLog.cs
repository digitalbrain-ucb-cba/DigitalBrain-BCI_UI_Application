using System.Text.Json;
using DigitalBrain.UI.Models;
using DigitalBrain.UI.UseCases.Ports;

namespace DigitalBrain.UI.Services.Storage;

/// <summary>
/// Guarda una línea JSON por sesión en <c>BACK/datos_capturados/sesiones.jsonl</c> (carpeta ignorada por Git),
/// para no mezclar metadatos con los <c>.txt</c> que lee <c>build-dataset</c>.
/// <c>DurationSeconds</c> es el tiempo guardado y <c>TotalSeconds</c> el que corrió el capturador
/// (las entradas anteriores a ese campo guardaban el total en <c>DurationSeconds</c>).
/// </summary>
public sealed class JsonlSessionLog(IBackendEnvironment environment) : ISessionLog
{
    private readonly object _lock = new();
    private Dictionary<string, double>? _durations;

    private string LogPath => Path.Combine(environment.BackendRoot, "datos_capturados", "sesiones.jsonl");

    public async Task AppendAsync(RecordingPlan plan, RecordingSummary result, CancellationToken cancellationToken = default)
    {
        var entry = new SessionLogEntry(
            result.FileName, result.FullPath, plan.SubjectId, plan.Movement.Label, plan.SessionNumber,
            plan.DurationSeconds, plan.CaptureFromSeconds, plan.Notes, result.ValidSamples, result.DiscardedSamples,
            result.Windows, DateTime.Now, plan.TotalSeconds);

        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        await File.AppendAllTextAsync(LogPath, JsonSerializer.Serialize(entry) + Environment.NewLine, cancellationToken);
        lock (_lock)
        {
            LoadDurations()[result.FileName] = plan.DurationSeconds;
        }
    }

    public double? FindDurationSeconds(string fileName)
    {
        lock (_lock)
        {
            return LoadDurations().TryGetValue(fileName, out var seconds) ? seconds : null;
        }
    }

    private Dictionary<string, double> LoadDurations()
    {
        if (_durations is not null) return _durations;
        _durations = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(LogPath)) return _durations;

        foreach (var line in File.ReadLines(LogPath))
        {
            try
            {
                if (JsonSerializer.Deserialize<SessionLogEntry>(line) is { } entry)
                    _durations[entry.FileName] = entry.DurationSeconds;
            }
            catch (JsonException)
            {
                // Una línea dañada no impide leer el resto del registro.
            }
        }
        return _durations;
    }

    private sealed record SessionLogEntry(
        string FileName, string FullPath, string SubjectId, string Movement, int SessionNumber,
        double DurationSeconds, double CaptureFromSeconds, string? Notes, long ValidSamples, long DiscardedSamples,
        int Windows, DateTime CapturedAt, double TotalSeconds = 0);
}
