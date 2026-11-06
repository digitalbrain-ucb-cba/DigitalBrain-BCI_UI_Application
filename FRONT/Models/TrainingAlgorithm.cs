namespace DigitalBrain.UI.Models;

/// <summary>Clasificador que se puede entrenar.</summary>
/// <param name="Id">Valor de <c>--algorithm</c>; debe existir en <c>MODEL_NAMES</c> del backend.</param>
/// <param name="DisplayName">Nombre visible.</param>
public sealed record TrainingAlgorithm(string Id, string DisplayName)
{
    public static readonly IReadOnlyList<TrainingAlgorithm> All =
    [
        new("random_forest", "Random forest"),
        new("lda", "LDA"),
        new("svm", "SVM"),
        new("extra_trees", "Extra trees"),
        new("gradient_boosting", "Gradient boosting"),
    ];

    public override string ToString() => DisplayName;
}
