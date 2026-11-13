using DigitalBrain.UI.Models;

namespace DigitalBrain.UI.UseCases.Ports;

/// <summary>Modelos entrenados: los de cada sujeto en <c>datos/&lt;sujeto&gt;/modelos/</c> y los anteriores de <c>artifacts/models/</c>.</summary>
public interface IModelStore
{
    /// <summary>Modelos de <paramref name="subject"/>, el más reciente primero.</summary>
    IReadOnlyList<ModelInfo> GetModels(string subject);

    /// <summary>Todos los modelos, el más reciente primero.</summary>
    IReadOnlyList<ModelInfo> GetAllModels();

    /// <summary>Ruta donde se guarda el modelo <paramref name="name"/> (sin extensión) de <paramref name="subject"/>.</summary>
    string ModelPath(string subject, string name);

    /// <summary>Guarda junto al modelo los datos de su entrenamiento.</summary>
    void SaveInfo(ModelInfo model);

    /// <summary>Borra el modelo y sus datos de entrenamiento.</summary>
    void Delete(ModelInfo model);
}
