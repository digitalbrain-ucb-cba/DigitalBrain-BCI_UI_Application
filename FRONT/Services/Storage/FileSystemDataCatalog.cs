using DigitalBrain.UI.Models;
using DigitalBrain.UI.UseCases.Ports;

namespace DigitalBrain.UI.Services.Storage;

/// <summary>
/// Catálogo leído directamente de <c>BACK/datos</c>: cada subcarpeta que no es de un movimiento predefinido
/// es un movimiento añadido, y cada subcarpeta de un movimiento es un sujeto. No hay archivo de configuración aparte,
/// así que lo que se ve en la interfaz es siempre lo que hay en disco.
/// </summary>
public sealed class FileSystemDataCatalog(IBackendEnvironment environment) : IDataCatalog
{
    private static readonly StringComparer NameComparer = StringComparer.CurrentCultureIgnoreCase;

    public IReadOnlyList<MovementClass> GetMovements()
    {
        var movements = MovementCatalog.BuiltIn.ToList();
        if (!Directory.Exists(environment.DataRoot)) return movements;

        var builtInFolders = MovementCatalog.BuiltIn.Select(MovementDirectory).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var customNames = Directory.EnumerateDirectories(environment.DataRoot)
            .Where(directory => !builtInFolders.Contains(Path.TrimEndingDirectorySeparator(directory)))
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => CatalogNames.Validate(name, "movimiento") is null && MovementCatalog.ToLabel(name).Length > 0)
            .Order(NameComparer);

        foreach (var name in customNames)
        {
            var movement = MovementCatalog.Custom(name, movements.Count - MovementCatalog.BuiltIn.Count);
            // Dos carpetas con la misma etiqueta mezclarían clases en el entrenamiento: se usa solo la primera.
            if (movements.All(m => m.Label != movement.Label))
                movements.Add(movement);
        }
        return movements;
    }

    public IReadOnlyList<string> GetSubjects(MovementClass movement)
    {
        var directory = MovementDirectory(movement);
        if (!Directory.Exists(directory)) return [];
        return Directory.EnumerateDirectories(directory)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => !name.StartsWith('.'))
            .Order(NameComparer)
            .ToList();
    }

    public CatalogResult<MovementClass> AddMovement(string name)
    {
        name = CatalogNames.Normalize(name);
        if (CatalogNames.Validate(name, "movimiento") is { } error)
            return CatalogResult<MovementClass>.Invalid(error);

        var label = MovementCatalog.ToLabel(name);
        if (label.Length == 0)
            return CatalogResult<MovementClass>.Invalid("El nombre del movimiento necesita al menos una letra o un número.");

        var existing = GetMovements().FirstOrDefault(m =>
            NameComparer.Equals(m.DisplayName, name) ||
            m.Label == label ||
            NameComparer.Equals(Path.GetFileName(MovementDirectory(m)), name));
        if (existing is not null)
            return CatalogResult<MovementClass>.Duplicate(existing, $"Ya existe el movimiento «{existing.DisplayName}».");

        Directory.CreateDirectory(Path.Combine(environment.DataRoot, name));
        return CatalogResult<MovementClass>.Created(GetMovements().First(m => m.Label == label));
    }

    public CatalogResult<string> AddSubject(MovementClass movement, string name)
    {
        name = CatalogNames.Normalize(name);
        if (CatalogNames.Validate(name, "sujeto") is { } error)
            return CatalogResult<string>.Invalid(error);

        var existing = GetSubjects(movement).FirstOrDefault(subject => NameComparer.Equals(subject, name));
        if (existing is not null)
            return CatalogResult<string>.Duplicate(existing, $"El sujeto «{existing}» ya existe en {movement.DisplayName}.");

        Directory.CreateDirectory(SubjectDirectory(movement, name));
        return CatalogResult<string>.Created(name);
    }

    public string MovementDirectory(MovementClass movement) =>
        Path.TrimEndingDirectorySeparator(environment.Resolve(movement.DataFolder));

    public string SubjectDirectory(MovementClass movement, string subject) =>
        Path.Combine(MovementDirectory(movement), subject);

    public IReadOnlyList<string> RecordingDirectories(MovementClass movement, string? subject)
    {
        IEnumerable<string> candidates = subject is null
            ? [MovementDirectory(movement), .. GetSubjects(movement).Select(s => SubjectDirectory(movement, s))]
            : [SubjectDirectory(movement, subject)];
        return candidates.Where(directory => CountCaptures(directory) > 0).ToList();
    }

    public int CountRecordings(MovementClass movement, string? subject) =>
        RecordingDirectories(movement, subject).Sum(CountCaptures);

    private static int CountCaptures(string directory) =>
        Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "*.txt").Count() : 0;
}
