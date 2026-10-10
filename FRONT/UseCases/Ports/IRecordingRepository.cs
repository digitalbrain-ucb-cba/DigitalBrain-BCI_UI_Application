using DigitalBrain.UI.Models;

namespace DigitalBrain.UI.UseCases.Ports;

/// <summary>Acceso de solo lectura a las capturas guardadas en <c>BACK/datos</c>.</summary>
public interface IRecordingRepository
{
    Task<int> CountAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RecordingSummary>> GetLatestAsync(int count, CancellationToken cancellationToken = default);
    Task<RecordingSummary?> AnalyzeAsync(string filePath, CancellationToken cancellationToken = default);
    /// <summary>Siguiente número de sesión libre para <paramref name="movement"/> en <paramref name="directory"/>.</summary>
    int NextSessionNumber(MovementClass movement, string directory);
    /// <summary>Carpetas de clase que contienen al menos una captura, para construir datasets.</summary>
    IReadOnlyList<(MovementClass Movement, string Directory)> FoldersWithRecordings();
}
