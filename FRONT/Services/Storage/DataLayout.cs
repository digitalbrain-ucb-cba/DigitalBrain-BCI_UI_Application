namespace DigitalBrain.UI.Services.Storage;

/// <summary>
/// Organización de <c>BACK/datos</c>: una carpeta por sujeto con sus grabaciones por movimiento y sus modelos.
/// <code>
/// datos/&lt;sujeto&gt;/movimientos/&lt;movimiento&gt;/*.txt
/// datos/&lt;sujeto&gt;/modelos/*.pkl
/// </code>
/// </summary>
public static class DataLayout
{
    public const string MovementsFolder = "movimientos";
    public const string ModelsFolder = "modelos";

    /// <summary>Una carpeta de <c>datos</c> es de un sujeto si tiene alguna de sus dos subcarpetas.</summary>
    public static bool IsSubjectDirectory(string directory) =>
        Directory.Exists(Path.Combine(directory, MovementsFolder)) || Directory.Exists(Path.Combine(directory, ModelsFolder));
}
