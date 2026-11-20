using DigitalBrain.UI.Models;

namespace DigitalBrain.UI.UseCases.Ports;

/// <summary>
/// Sujetos y movimientos disponibles. Se guardan como carpetas: cada sujeto tiene la suya en <c>BACK/datos/&lt;sujeto&gt;/</c>
/// y dentro una por tipo de entrenamiento (<c>MI</c>, <c>M</c>) con <c>movimientos/&lt;movimiento&gt;/*.txt</c> y <c>modelos/*.pkl</c>.
/// </summary>
public interface IDataCatalog
{
    /// <summary>Sujetos en orden alfabético, tengan o no ya alguna carpeta de tipo.</summary>
    IReadOnlyList<string> GetSubjects();

    /// <summary>Crea la carpeta de un sujeto nuevo, sin tipos: se crean al elegirlos con <see cref="EnsureType"/>.</summary>
    CatalogResult<string> AddSubject(string name);

    /// <summary>El sujeto ya tiene la carpeta de <paramref name="type"/>.</summary>
    bool HasType(string subject, ModelType type);

    /// <summary>Crea, si falta, la carpeta de <paramref name="type"/> del sujeto con sus carpetas de movimientos y modelos.</summary>
    void EnsureType(string subject, ModelType type);

    /// <summary>Movimientos predefinidos seguidos de los añadidos a cualquier sujeto y tipo.</summary>
    IReadOnlyList<MovementClass> GetMovements();

    /// <summary>Crea un movimiento nuevo en la carpeta de movimientos de <paramref name="subject"/> para <paramref name="type"/>.</summary>
    CatalogResult<MovementClass> AddMovement(string subject, ModelType type, string name);

    string SubjectDirectory(string subject);
    string TypeDirectory(string subject, ModelType type);
    string MovementDirectory(string subject, ModelType type, MovementClass movement);
    string ModelsDirectory(string subject, ModelType type);

    /// <summary>Número de capturas <c>.txt</c> de <paramref name="subject"/> en <paramref name="type"/> para <paramref name="movement"/>.</summary>
    int CountRecordings(string subject, ModelType type, MovementClass movement);
}

/// <param name="Value">Elemento creado o, si <paramref name="AlreadyExists"/>, el que ya existía.</param>
/// <param name="Error">Motivo por el que no se creó.</param>
public sealed record CatalogResult<T>(T? Value, string? Error, bool AlreadyExists = false)
{
    public bool Succeeded => Error is null;
    public static CatalogResult<T> Created(T value) => new(value, null);
    public static CatalogResult<T> Invalid(string error) => new(default, error);
    public static CatalogResult<T> Duplicate(T existing, string error) => new(existing, error, AlreadyExists: true);
}
