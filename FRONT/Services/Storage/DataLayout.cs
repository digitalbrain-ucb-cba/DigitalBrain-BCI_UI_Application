using DigitalBrain.UI.Models;

namespace DigitalBrain.UI.Services.Storage;

/// <summary>
/// Organización de <c>BACK/datos</c>: una carpeta por sujeto y dentro una por tipo de entrenamiento (<see cref="ModelType"/>),
/// con sus grabaciones por movimiento y sus modelos.
/// <code>
/// datos/&lt;sujeto&gt;/&lt;MI|M&gt;/movimientos/&lt;movimiento&gt;/*.txt
/// datos/&lt;sujeto&gt;/&lt;MI|M&gt;/modelos/*.pkl
/// </code>
/// La carpeta del sujeto se crea al añadirlo y la del tipo al elegirlo para capturar.
/// </summary>
public static class DataLayout
{
    public const string MovementsFolder = "movimientos";
    public const string ModelsFolder = "modelos";

    /// <summary>Tiene alguna carpeta de tipo (<c>MI</c> o <c>M</c>).</summary>
    public static bool HasTypeFolders(string directory) =>
        ModelType.All.Any(type => Directory.Exists(Path.Combine(directory, type.Folder)));

    /// <summary>
    /// Sujeto de la organización anterior, sin tipo: sus carpetas de movimientos y modelos cuelgan directamente de él.
    /// </summary>
    public static bool HasUntypedFolders(string directory) =>
        Directory.Exists(Path.Combine(directory, MovementsFolder)) || Directory.Exists(Path.Combine(directory, ModelsFolder));
}
