using DigitalBrain.UI.Models;
using DigitalBrain.UI.UseCases.Ports;

namespace DigitalBrain.UI.Services.Storage;

/// <summary>
/// Catálogo leído directamente de <c>BACK/datos</c> con la organización de <see cref="DataLayout"/>: cada carpeta es un sujeto,
/// y cada carpeta de movimientos de sus tipos que no es de un movimiento predefinido es un movimiento añadido. No hay archivo
/// de configuración aparte, así que lo que se ve en la interfaz es siempre lo que hay en disco.
/// </summary>
public sealed class FileSystemDataCatalog(IBackendEnvironment environment) : IDataCatalog
{
    private static readonly StringComparer NameComparer = StringComparer.CurrentCultureIgnoreCase;

    /// <summary>
    /// Todas las carpetas de <c>datos</c> salvo las ocultas y las de sujetos sin tipo de la organización anterior,
    /// que se dejan como están. Un sujeto recién añadido aún no tiene carpetas de tipo y también cuenta.
    /// </summary>
    public IReadOnlyList<string> GetSubjects() =>
        Directory.Exists(environment.DataRoot)
            ? Directory.EnumerateDirectories(environment.DataRoot)
                .Where(directory => DataLayout.HasTypeFolders(directory) || !DataLayout.HasUntypedFolders(directory))
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
            return CatalogResult<string>.Invalid($"Ya hay en datos una carpeta «{name}» sin tipo MI o M; elige otro nombre.");

        Directory.CreateDirectory(SubjectDirectory(name));
        return CatalogResult<string>.Created(name);
    }

    public bool HasType(string subject, ModelType type) => Directory.Exists(TypeDirectory(subject, type));

    public void EnsureType(string subject, ModelType type)
    {
        Directory.CreateDirectory(Path.Combine(TypeDirectory(subject, type), DataLayout.MovementsFolder));
        Directory.CreateDirectory(ModelsDirectory(subject, type));
    }

    public IReadOnlyList<MovementClass> GetMovements()
    {
        var movements = MovementCatalog.BuiltIn.ToList();
        var customNames = GetSubjects()
            .SelectMany(subject => ModelType.All.Select(type => Path.Combine(TypeDirectory(subject, type), DataLayout.MovementsFolder)))
            .SelectMany(SafeDirectories)
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

    public CatalogResult<MovementClass> AddMovement(string subject, ModelType type, string name)
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

        EnsureType(subject, type);
        Directory.CreateDirectory(Path.Combine(TypeDirectory(subject, type), DataLayout.MovementsFolder, name));
        return CatalogResult<MovementClass>.Created(GetMovements().First(m => m.Label == label));
    }

    public string SubjectDirectory(string subject) => Path.Combine(environment.DataRoot, subject);

    public string TypeDirectory(string subject, ModelType type) => Path.Combine(SubjectDirectory(subject), type.Folder);

    public string MovementDirectory(string subject, ModelType type, MovementClass movement) =>
        Path.Combine(TypeDirectory(subject, type), DataLayout.MovementsFolder, movement.FolderName);

    public string ModelsDirectory(string subject, ModelType type) => Path.Combine(TypeDirectory(subject, type), DataLayout.ModelsFolder);

    public int CountRecordings(string subject, ModelType type, MovementClass movement)
    {
        var directory = MovementDirectory(subject, type, movement);
        return Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "*.txt").Count() : 0;
    }

    private static IEnumerable<string> SafeDirectories(string directory) =>
        Directory.Exists(directory) ? Directory.EnumerateDirectories(directory) : [];
}
