using DigitalBrain.UI.Models;
using DigitalBrain.UI.UseCases.Ports;

namespace DigitalBrain.UI.Services.Storage;

/// <summary>
/// Lee las capturas <c>.txt</c> de <c>BACK/datos</c> con las reglas de validez del backend. Una captura en
/// <c>datos/&lt;sujeto&gt;/movimientos/&lt;movimiento&gt;/</c> pertenece a ese sujeto y a ese movimiento.
/// </summary>
public sealed class FileSystemRecordingRepository(IBackendEnvironment environment, ISessionLog sessionLog, IDataCatalog catalog) : IRecordingRepository
{
    public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => EnumerateCaptures().Count(), cancellationToken);

    public Task<IReadOnlyList<RecordingSummary>> GetLatestAsync(int count, CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<RecordingSummary>>(() =>
        {
            var movements = catalog.GetMovements();
            return EnumerateCaptures()
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => TruncateToMinute(file.LastWriteTime))
                .ThenByDescending(file => SessionNumberOf(file, movements))
                .Take(count)
                .Select(file => Analyze(file.FullName, movements))
                .OfType<RecordingSummary>()
                .ToList();
        }, cancellationToken);

    public Task<RecordingSummary?> AnalyzeAsync(string filePath, CancellationToken cancellationToken = default) =>
        Task.Run(() => Analyze(filePath, catalog.GetMovements()), cancellationToken);

    public int NextSessionNumber(MovementClass movement, string directory)
    {
        if (!Directory.Exists(directory)) return 1;
        var numbers = Directory.EnumerateFiles(directory, "*.txt")
            .Select(path => movement.TryGetSessionNumber(Path.GetFileName(path)))
            .OfType<int>()
            .ToList();
        return numbers.Count == 0 ? 1 : numbers.Max() + 1;
    }

    private IEnumerable<string> EnumerateCaptures() =>
        Directory.Exists(environment.DataRoot)
            ? Directory.EnumerateFiles(environment.DataRoot, "*.txt", SearchOption.AllDirectories)
            : [];

    private RecordingSummary? Analyze(string filePath, IReadOnlyList<MovementClass> movements)
    {
        if (!File.Exists(filePath)) return null;

        long valid = 0, discarded = 0;
        foreach (var line in File.ReadLines(filePath))
        {
            if (!SignalValidity.TryParseLine(line, 0, out var sample)) continue;
            if (sample.IsSaturated) discarded++;
            else valid++;
        }

        var file = new FileInfo(filePath);
        var duration = sessionLog.FindDurationSeconds(file.Name)
                       ?? Math.Round((valid + discarded) / AcquisitionParameters.SampleRateHz);
        var (movement, subject) = Classify(file, movements);
        return new RecordingSummary(file.Name, file.FullName, movement, subject, duration, valid, discarded, file.LastWriteTime);
    }

    /// <summary>Movimiento y sujeto según la carpeta <c>&lt;sujeto&gt;/movimientos/&lt;movimiento&gt;/</c>.</summary>
    private static (MovementClass? Movement, string? Subject) Classify(FileInfo file, IReadOnlyList<MovementClass> movements)
    {
        var directory = file.Directory;
        if (directory?.Parent is { } parent && parent.Parent is { } subject &&
            string.Equals(parent.Name, DataLayout.MovementsFolder, StringComparison.OrdinalIgnoreCase))
        {
            var movement = movements.FirstOrDefault(m => string.Equals(m.FolderName, directory.Name, StringComparison.CurrentCultureIgnoreCase));
            return (movement, subject.Name);
        }
        return (movements.FirstOrDefault(m => m.TryGetSessionNumber(file.Name) is not null), null);
    }

    private static int SessionNumberOf(FileInfo file, IReadOnlyList<MovementClass> movements) =>
        movements.Select(m => m.TryGetSessionNumber(file.Name)).OfType<int>().DefaultIfEmpty(0).Max();

    private static DateTime TruncateToMinute(DateTime time) =>
        new(time.Year, time.Month, time.Day, time.Hour, time.Minute, 0, time.Kind);
}
