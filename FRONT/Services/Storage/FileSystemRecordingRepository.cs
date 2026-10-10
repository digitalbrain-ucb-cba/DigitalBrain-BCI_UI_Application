using DigitalBrain.UI.Models;
using DigitalBrain.UI.UseCases.Ports;

namespace DigitalBrain.UI.Services.Storage;

/// <summary>Lee las capturas <c>.txt</c> de <c>BACK/datos</c> con las reglas de validez del backend.</summary>
public sealed class FileSystemRecordingRepository(IBackendEnvironment environment, ISessionLog sessionLog) : IRecordingRepository
{
    public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => EnumerateCaptures().Count(), cancellationToken);

    public Task<IReadOnlyList<RecordingSummary>> GetLatestAsync(int count, CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<RecordingSummary>>(() =>
        {
            return EnumerateCaptures()
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => TruncateToMinute(file.LastWriteTime))
                .ThenByDescending(file => SessionNumberOf(file))
                .Take(count)
                .Select(file => Analyze(file.FullName))
                .OfType<RecordingSummary>()
                .ToList();
        }, cancellationToken);

    public Task<RecordingSummary?> AnalyzeAsync(string filePath, CancellationToken cancellationToken = default) =>
        Task.Run(() => Analyze(filePath), cancellationToken);

    public int NextSessionNumber(MovementClass movement, string directory)
    {
        if (!Directory.Exists(directory)) return 1;
        var numbers = Directory.EnumerateFiles(directory, "*.txt")
            .Select(path => movement.TryGetSessionNumber(Path.GetFileName(path)))
            .OfType<int>()
            .ToList();
        return numbers.Count == 0 ? 1 : numbers.Max() + 1;
    }

    public IReadOnlyList<(MovementClass Movement, string Directory)> FoldersWithRecordings() =>
        MovementCatalog.All
            .Select(movement => (Movement: movement, Directory: environment.Resolve(movement.DataFolder)))
            .Where(entry => Directory.Exists(entry.Directory) && Directory.EnumerateFiles(entry.Directory, "*.txt").Any())
            .ToList();

    private IEnumerable<string> EnumerateCaptures() =>
        Directory.Exists(environment.DataRoot)
            ? Directory.EnumerateFiles(environment.DataRoot, "*.txt", SearchOption.AllDirectories)
            : [];

    private RecordingSummary? Analyze(string filePath)
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
        return new RecordingSummary(file.Name, file.FullName, MovementOf(file), duration, valid, discarded, file.LastWriteTime);
    }

    private MovementClass? MovementOf(FileInfo file)
    {
        var directory = Path.TrimEndingDirectorySeparator(file.DirectoryName ?? "");
        return MovementCatalog.All.FirstOrDefault(m =>
                   string.Equals(Path.TrimEndingDirectorySeparator(environment.Resolve(m.DataFolder)), directory, StringComparison.OrdinalIgnoreCase))
               ?? MovementCatalog.All.FirstOrDefault(m => m.TryGetSessionNumber(file.Name) is not null);
    }

    private static int SessionNumberOf(FileInfo file) =>
        MovementCatalog.All.Select(m => m.TryGetSessionNumber(file.Name)).OfType<int>().DefaultIfEmpty(0).Max();

    private static DateTime TruncateToMinute(DateTime time) =>
        new(time.Year, time.Month, time.Day, time.Hour, time.Minute, 0, time.Kind);
}
