namespace DigitalBrain.UI.Models;

/// <summary>
/// Algoritmo clasificador disponible en el backend para entrenar.
/// </summary>
/// <param name="Id">Identificador reconocido por <c>digital_brain.cli train --algorithm</c>.</param>
/// <param name="DisplayName">Nombre directo y legible que se muestra en la interfaz.</param>
/// <param name="Description">Descripción concisa de la técnica.</param>
public sealed record TrainingAlgorithm(
    string Id,
    string DisplayName,
    string Description)
{
    public override string ToString() => DisplayName;

    public static readonly IReadOnlyList<TrainingAlgorithm> All =
    [
        new("random_forest", "Random Forest", "Ensamble de árboles de decisión"),
        new("lda", "LDA", "Análisis discriminante lineal"),
        new("svm", "SVM", "Máquina de vectores de soporte con kernel RBF"),
        new("extra_trees", "Extra Trees", "Árboles extremadamente aleatorizados"),
        new("gradient_boosting", "Gradient Boosting", "Potenciación de gradiente secuencial"),
    ];

    public static readonly TrainingAlgorithm Default = All[0];
}
