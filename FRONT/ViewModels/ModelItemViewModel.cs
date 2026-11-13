using System.Globalization;
using DigitalBrain.UI.Models;

namespace DigitalBrain.UI.ViewModels;

/// <summary>Fila de la lista de modelos de un sujeto.</summary>
public sealed class ModelItemViewModel(ModelInfo model)
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    public ModelInfo Model { get; } = model;
    public string Name => Model.Name;
    public string FilePath => Model.FilePath;

    public string DetailText { get; } = string.Join(" · ", new[]
    {
        model.Algorithm?.DisplayName,
        model.Accuracy is { } accuracy ? $"exactitud {accuracy.ToString("P1", Es)}" : null,
        model.TrainedAt.ToString("dd/MM/yy HH:mm", Es),
    }.OfType<string>());
}
