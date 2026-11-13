using DigitalBrain.UI.Models;
using DigitalBrain.UI.UseCases.Ports;

namespace DigitalBrain.UI.Services.Storage;

/// <summary>
/// Catálogo leído directamente de <c>BACK/datos</c> con la organización de <see cref="DataLayout"/>: cada carpeta de sujeto es
/// un sujeto, y cada carpeta de sus movimientos que no es de un movimiento predefinido es un movimiento añadido. No hay archivo
/// de configuración aparte, así que lo que se ve en la interfaz es siempre lo que hay en disco.
/// </summary>
public sealed class FileSystemDataCatalog(IBackendEnvironment environment) : IDataCatalog
{
    private static readonly StringComparer NameComparer = StringComparer.CurrentCultureIgnoreCase;

    public IReadOnlyList<string> GetSubjects() =>
        Directory.Exists(environment.DataRoot)
            ? Directory.EnumerateDirectories(environment.DataRoot)
                .Where(DataLayout.IsSubjectDirectory)
                .Select(Path.GetFileName)
                .OfType<string>()
                .Where(name => !name.StartsWith('.'))
                .Order(NameComparer)
                .ToList()
            : [];

    public CatalogResult<string> AddSubject(string name)
    {
        name = CatalogNames.Normalize(name);
        if (CatalogNames.Validate(name, "sujeto") is { } error)
            return CatalogResult<string>.Invalid(error);

        var existing = GetSubjects().FirstOrDefault(subject => NameComparer.Equals(subject, name));
        if (existing is not null)
            return CatalogResult<string>.Duplicate(existing, $"El sujeto «{existing}» ya existe.");
        if (Directory.Exists(SubjectDirectory(name)))
            return CatalogResult<string>.Invalid($"Ya hay en datos una carpeta «{name}» que no es de un sujeto; elige otro nombre.");

        Directory.CreateDirectory(Path.Combine(SubjectDirectory(name), DataLayout.MovementsFolder));
        Directory.CreateDirectory(ModelsDirectory(name));
        return CatalogResult<string>.Created(name);
    }

    public IReadOnlyList<MovementClass> GetMovements()
    {
        var movements = MovementCatalog.BuiltIn.ToList();
        var customNames = GetSubjects()
            .SelectMany(subject => SafeDirectories(Path.Combine(SubjectDirectory(subject), DataLayout.MovementsFolder)))
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => !MovementCatalog.BuiltIn.Any(builtIn => NameComparer.Equals(builtIn.FolderName, name)))
            .Where(name => CatalogNames.Validate(name, "movimiento") is null && MovementCatalog.ToLabel(name).Length > 0)
            .Distinct(NameComparer)
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

    public CatalogResult<MovementClass> AddMovement(string subject, string name)
    {
        name = CatalogNames.Normalize(name);
        if (CatalogNames.Validate(name, "movimiento") is { } error)
            return CatalogResult<MovementClass>.Invalid(error);

        var label = MovementCatalog.ToLabel(name);
        if (label.Length == 0)
            return CatalogResult<MovementClass>.Invalid("El nombre del movimiento necesita al menos una letra o un número.");

        var existing = GetMovements().FirstOrDefault(m => NameComparer.Equals(m.DisplayName, name) || m.Label == label);
        if (existing is not null)
            return CatalogResult<MovementClass>.Duplicate(existing, $"Ya existe el movimiento «{existing.DisplayName}».");

        Directory.CreateDirectory(Path.Combine(SubjectDirectory(subject), DataLayout.MovementsFolder, name));
        return CatalogResult<MovementClass>.Created(GetMovements().First(m => m.Label == label));
    }

    public string SubjectDirectory(string subject) => Path.Combine(environment.DataRoot, subject);

    public string MovementDirectory(string subject, MovementClass movement) =>
        Path.Combine(SubjectDirectory(subject), DataLayout.MovementsFolder, movement.FolderName);

    public string ModelsDirectory(string subject) => Path.Combine(SubjectDirectory(subject), DataLayout.ModelsFolder);

    public int CountRecordings(string subject, MovementClass movement)
    {
        var directory = MovementDirectory(subject, movement);
        return Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "*.txt").Count() : 0;
    }

    private static IEnumerable<string> SafeDirectories(string directory) =>
        Directory.Exists(directory) ? Directory.EnumerateDirectories(directory) : [];
}
