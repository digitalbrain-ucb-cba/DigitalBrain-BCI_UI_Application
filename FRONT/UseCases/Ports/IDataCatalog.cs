using DigitalBrain.UI.Models;

namespace DigitalBrain.UI.UseCases.Ports;

/// <summary>
/// Movimientos y sujetos disponibles. Se guardan como carpetas: <c>BACK/datos/&lt;movimiento&gt;/&lt;sujeto&gt;/*.txt</c>.
/// Las capturas anteriores que están sueltas en la carpeta del movimiento no pertenecen a ningún sujeto.
/// </summary>
public interface IDataCatalog
{
    /// <summary>Movimientos predefinidos seguidos de los añadidos desde la interfaz.</summary>
    IReadOnlyList<MovementClass> GetMovements();

    /// <summary>Sujetos (subcarpetas) que tiene <paramref name="movement"/>, en orden alfabético.</summary>
    IReadOnlyList<string> GetSubjects(MovementClass movement);

    /// <summary>Crea la carpeta de un movimiento nuevo.</summary>
    CatalogResult<MovementClass> AddMovement(string name);

    /// <summary>Crea la carpeta de un sujeto nuevo dentro de <paramref name="movement"/>.</summary>
    CatalogResult<string> AddSubject(MovementClass movement, string name);

    string MovementDirectory(MovementClass movement);
    string SubjectDirectory(MovementClass movement, string subject);

    /// <summary>
    /// Carpetas con capturas de <paramref name="movement"/>: la de <paramref name="subject"/> o, si es <c>null</c>,
    /// las de todos los sujetos más la del movimiento si tiene capturas anteriores sueltas.
    /// </summary>
    IReadOnlyList<string> RecordingDirectories(MovementClass movement, string? subject);

    /// <summary>Número de capturas <c>.txt</c> que usaría <see cref="RecordingDirectories"/>.</summary>
    int CountRecordings(MovementClass movement, string? subject);
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
