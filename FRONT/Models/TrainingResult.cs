namespace DigitalBrain.UI.Models;

/// <summary>
/// Resultado del entrenamiento de un clasificador.
/// </summary>
public record TrainingResult(
    string ModelPath,
    double Accuracy,
    string ClassifierName,
    DateTime CompletedAt
);
