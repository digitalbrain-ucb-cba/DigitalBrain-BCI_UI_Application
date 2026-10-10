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

/// <summary>Clases conocidas, deducidas de las carpetas de <c>BACK/datos</c>.</summary>
public static class MovementCatalog
{
    public static readonly IReadOnlyList<MovementClass> All =
    [
        new("brazo_derecho", "Brazo derecho", @"datos\Right", "Brazo derecho {0}b", "#4A6478"),
        new("pierna_izquierda", "Pierna izquierda", @"datos\left", "Pierna izquierda {0}", "#B5654A"),
        new("hombro_derecho", "Hombro derecho", @"datos\datos_hombro_derecho", "Hombro derecho_{0}", "#C49A3A"),
    ];
}
