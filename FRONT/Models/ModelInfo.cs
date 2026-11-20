namespace DigitalBrain.UI.Models;

/// <summary>Modelo <c>.pkl</c> guardado, con los datos de su entrenamiento si se conocen.</summary>
/// <param name="Subject">Sujeto en cuya carpeta está, o <c>null</c> si es de la carpeta anterior <c>artifacts/models</c>.</param>
/// <param name="Type">Tipo de entrenamiento en cuya carpeta está, o <c>null</c> si no tiene sujeto.</param>
/// <param name="AlgorithmId">Valor de <c>--algorithm</c> con que se entrenó.</param>
/// <param name="Accuracy">Exactitud de 0 a 1 sobre la división aleatoria de <c>train</c>.</param>
/// <param name="Movements">Etiquetas de los movimientos que aprendió.</param>
public sealed record ModelInfo(
    string FilePath,
    string? Subject,
    ModelType? Type,
    string? AlgorithmId,
    double? Accuracy,
    DateTime TrainedAt,
    IReadOnlyList<string> Movements)
{
    public string Name => Path.GetFileNameWithoutExtension(FilePath);
    public string FileName => Path.GetFileName(FilePath);
    public TrainingAlgorithm? Algorithm => TrainingAlgorithm.All.FirstOrDefault(a => a.Id == AlgorithmId);
}
