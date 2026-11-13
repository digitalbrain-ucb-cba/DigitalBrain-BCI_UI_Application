using DigitalBrain.UI.Models;

namespace DigitalBrain.UI.UseCases.Ports;

/// <summary>
/// Sujetos y movimientos disponibles. Se guardan como carpetas: cada sujeto tiene las suyas en
/// <c>BACK/datos/&lt;sujeto&gt;/</c>, con <c>movimientos/&lt;movimiento&gt;/*.txt</c> y <c>modelos/*.pkl</c>.
/// </summary>
public interface IDataCatalog
{
    /// <summary>Sujetos en orden alfabético.</summary>
    IReadOnlyList<string> GetSubjects();

    /// <summary>Crea la carpeta de un sujeto nuevo con sus carpetas de movimientos y modelos.</summary>
    CatalogResult<string> AddSubject(string name);

    /// <summary>Movimientos predefinidos seguidos de los añadidos a cualquier sujeto.</summary>
    IReadOnlyList<MovementClass> GetMovements();

    /// <summary>Crea un movimiento nuevo en la carpeta de movimientos de <paramref name="subject"/>.</summary>
    CatalogResult<MovementClass> AddMovement(string subject, string name);

    string SubjectDirectory(string subject);
    string MovementDirectory(string subject, MovementClass movement);
    string ModelsDirectory(string subject);

    /// <summary>Número de capturas <c>.txt</c> de <paramref name="subject"/> para <paramref name="movement"/>.</summary>
    int CountRecordings(string subject, MovementClass movement);
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
