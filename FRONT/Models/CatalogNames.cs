using System.Text.RegularExpressions;

namespace DigitalBrain.UI.Models;

/// <summary>
/// Reglas para los nombres de movimientos y sujetos, que se usan tal cual como nombres de carpeta
/// en <c>BACK/datos/&lt;movimiento&gt;/&lt;sujeto&gt;/</c>.
/// </summary>
public static class CatalogNames
{
    public const int MaxLength = 40;

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Quita espacios sobrantes al principio, al final y repetidos.</summary>
    public static string Normalize(string? name) =>
        Regex.Replace(name ?? "", @"\s+", " ").Trim();

    /// <summary>Devuelve el motivo por el que <paramref name="name"/> no sirve como carpeta, o <c>null</c> si es válido.</summary>
    /// <param name="kind">«sujeto» o «movimiento», para el mensaje.</param>
    public static string? Validate(string name, string kind)
    {
        if (string.IsNullOrWhiteSpace(name))
            return $"Escribe el nombre del {kind}.";
        if (name.Length > MaxLength)
            return $"El nombre del {kind} admite como máximo {MaxLength} caracteres.";
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('{') || name.Contains('}'))
            return "El nombre no puede contener \\ / : * ? \" < > | { }.";
        if (name.StartsWith('.') || name.EndsWith('.'))
            return "El nombre no puede empezar ni terminar en punto.";
        if (ReservedNames.Contains(name))
            return $"«{name}» es un nombre reservado de Windows.";
        return null;
    }
}
