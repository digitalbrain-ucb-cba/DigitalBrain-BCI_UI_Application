using DigitalBrain.UI.Models;

namespace DigitalBrain.UI.UseCases.Ports;

/// <summary>
/// Modelos entrenados: los de cada sujeto y tipo en <c>datos/&lt;sujeto&gt;/&lt;MI|M&gt;/modelos/</c> y los anteriores de
/// <c>artifacts/models/</c>.
/// </summary>
public interface IModelStore
{
    /// <summary>Modelos de <paramref name="subject"/> para <paramref name="type"/>, el más reciente primero.</summary>
    IReadOnlyList<ModelInfo> GetModels(string subject, ModelType type);

    /// <summary>Todos los modelos, el más reciente primero.</summary>
    IReadOnlyList<ModelInfo> GetAllModels();

    /// <summary>Ruta donde se guarda el modelo <paramref name="name"/> (sin extensión) de <paramref name="subject"/> para <paramref name="type"/>.</summary>
    string ModelPath(string subject, ModelType type, string name);

    /// <summary>Guarda junto al modelo los datos de su entrenamiento.</summary>
    void SaveInfo(ModelInfo model);

    /// <summary>Borra el modelo y sus datos de entrenamiento.</summary>
    void Delete(ModelInfo model);
}
