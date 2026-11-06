namespace DigitalBrain.UI.Models;

/// <summary>
/// Tipo de modelo al que pertenece un sujeto. Va como prefijo en el nombre de su carpeta (<c>ModelMI_Arturo</c>),
/// y de ahí pasa al nombre del modelo entrenado. El separador evita confundir <c>ModelM</c> + <c>Iguel</c> con <c>ModelMI</c> + <c>guel</c>.
/// </summary>
public sealed record ModelType(string Prefix)
{
    private const char Separator = '_';

    public static readonly IReadOnlyList<ModelType> All = [new("ModelMI"), new("ModelM")];

    /// <summary>Tipo de un sujeto según su prefijo, o <c>null</c> si su carpeta no lleva ninguno.</summary>
    public static ModelType? Of(string? subject) =>
        All.FirstOrDefault(type => subject?.StartsWith(type.Prefix + Separator, StringComparison.OrdinalIgnoreCase) == true);

    /// <summary>Añade el prefijo sin duplicarlo; un nombre vacío sigue vacío para que la validación lo rechace.</summary>
    public string Apply(string? name)
    {
        var normalized = CatalogNames.Normalize(name);
        if (Of(normalized) is { } current)
            normalized = normalized[(current.Prefix.Length + 1)..].TrimStart();
        return normalized.Length == 0 ? "" : $"{Prefix}{Separator}{normalized}";
    }

    public override string ToString() => Prefix;
}
