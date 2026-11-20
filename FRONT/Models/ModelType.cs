namespace DigitalBrain.UI.Models;

/// <summary>
/// Tipo de entrenamiento. Cada sujeto tiene una subcarpeta por tipo (<c>datos/Arturo/MI/</c>) con sus movimientos y modelos;
/// se crea al elegir el tipo en Capturar.
/// </summary>
/// <param name="Folder">Nombre de la subcarpeta del sujeto y abreviatura visible.</param>
/// <param name="DisplayName">Nombre completo.</param>
public sealed record ModelType(string Folder, string DisplayName)
{
    public static readonly ModelType MotorImagery = new("MI", "Motor Imagery");
    public static readonly ModelType MotorMovement = new("M", "Motor Movement");

    public static readonly IReadOnlyList<ModelType> All = [MotorImagery, MotorMovement];

    /// <summary>
    /// Prefijo con que se guardaban antes los sujetos (<c>ModelMI_Arturo</c>); solo lo usa la migración a la organización actual.
    /// </summary>
    public string LegacyPrefix => $"Model{Folder}_";

    /// <summary>Tipo cuya carpeta es <paramref name="folder"/>, o <c>null</c> si no es la de ningún tipo.</summary>
    public static ModelType? FromFolder(string? folder) =>
        All.FirstOrDefault(type => string.Equals(type.Folder, folder, StringComparison.OrdinalIgnoreCase));

    public override string ToString() => $"{Folder} · {DisplayName}";
}
