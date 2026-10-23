using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DigitalBrain.UI.Models;

/// <summary>
/// Movimiento o clase que se graba. Define la etiqueta usada en el dataset, la carpeta de datos
/// relativa a <c>BACK/</c> y el patrón de nombre de archivo que ya siguen las capturas existentes.
/// </summary>
/// <param name="Label">Etiqueta que recibe <c>build-dataset</c> (p. ej. <c>brazo_derecho</c>).</param>
/// <param name="DisplayName">Nombre visible.</param>
/// <param name="DataFolder">Carpeta relativa a <c>BACK/</c>.</param>
/// <param name="FileNameTemplate">Patrón con <c>{0}</c> para el número de sesión, sin extensión.</param>
/// <param name="AccentHex">Color con que se marca la clase en la lista de grabaciones.</param>
public sealed record MovementClass(
    string Label,
    string DisplayName,
    string DataFolder,
    string FileNameTemplate,
    string AccentHex)
{
    public string FileNameFor(int sessionNumber) =>
        string.Format(FileNameTemplate, sessionNumber) + ".txt";

    /// <summary>Devuelve el número de sesión si <paramref name="fileName"/> sigue el patrón de esta clase.</summary>
    public int? TryGetSessionNumber(string fileName)
    {
        var parts = FileNameTemplate.Split("{0}");
        var pattern = "^" + Regex.Escape(parts[0]) + @"(\d+)" + Regex.Escape(parts.Length > 1 ? parts[1] : "") + @"\.txt$";
        var match = Regex.Match(fileName, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? int.Parse(match.Groups[1].Value) : null;
    }

    public override string ToString() => DisplayName;
}

/// <summary>
/// Movimientos predefinidos (con las carpetas y nombres de archivo que ya usan las capturas existentes)
/// y fábrica de los que se añaden desde la interfaz.
/// </summary>
public static class MovementCatalog
{
    public static readonly IReadOnlyList<MovementClass> BuiltIn =
    [
        new("brazo_derecho", "Brazo derecho", @"datos\Right", "Brazo derecho {0}b", "#4A6478"),
        new("pierna_izquierda", "Pierna izquierda", @"datos\left", "Pierna izquierda {0}", "#B5654A"),
        new("hombro_derecho", "Hombro derecho", @"datos\datos_hombro_derecho", "Hombro derecho_{0}", "#C49A3A"),
    ];

    private static readonly string[] CustomAccents = ["#5E8B6A", "#7A5C8F", "#3F7F8C", "#9A5B6E", "#8A7A3A", "#5A6E9A"];

    /// <summary>Movimiento añadido desde la interfaz: su carpeta en <c>datos/</c> lleva el nombre visible.</summary>
    public static MovementClass Custom(string displayName, int index) =>
        new(ToLabel(displayName), displayName, Path.Combine("datos", displayName), displayName + " {0}",
            CustomAccents[index % CustomAccents.Length]);

    /// <summary>Etiqueta del dataset: minúsculas sin tildes, con «_» en lugar de espacios y símbolos.</summary>
    public static string ToLabel(string displayName)
    {
        var builder = new StringBuilder();
        foreach (var c in displayName.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(char.IsAsciiLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_');
        }
        return Regex.Replace(builder.ToString(), "_+", "_").Trim('_');
    }
}
