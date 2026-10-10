using DigitalBrain.UI.Models;

namespace DigitalBrain.UI.UseCases.Ports;

/// <summary>Registro de metadatos (sujeto, notas, duración) de cada sesión grabada desde la interfaz.</summary>
public interface ISessionLog
{
    Task AppendAsync(RecordingPlan plan, RecordingSummary result, CancellationToken cancellationToken = default);
    /// <summary>Duración configurada de una grabación registrada, si existe.</summary>
    double? FindDurationSeconds(string fileName);
}
