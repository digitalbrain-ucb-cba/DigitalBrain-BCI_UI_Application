using DigitalBrain.UI.UseCases.Ports;
using DigitalBrain.UI.Models;
using DigitalBrain.UI.Services.Backend;

namespace DigitalBrain.UI.UseCases;

/// <summary>
/// Caso de uso «entrenar modelo»: para cada movimiento elegido crea un CSV por carpeta de capturas
/// (la del sujeto elegido o la de cada sujeto) con <c>build-dataset</c> y entrena un único modelo con todos ellos (<c>train</c>).
/// </summary>
public sealed class TrainModelUseCase(
    IBackendBridge backend,
    IDataCatalog catalog,
    IBackendEnvironment environment)
{
    public const string DefaultAlgorithm = "random_forest";

    public async Task<TrainModelResult> ExecuteAsync(
        IReadOnlyList<TrainingSelection> selections,
        IProgress<string>? progress = null,
        string algorithm = DefaultAlgorithm)
    {
        if (selections.Select(s => s.Movement.Label).Distinct().Count() < 2)
            return TrainModelResult.Failure("Elige al menos dos movimientos para entrenar.");

        var sources = new List<(TrainingSelection Selection, IReadOnlyList<string> Directories)>();
        foreach (var selection in selections)
        {
            var directories = catalog.RecordingDirectories(selection.Movement, selection.Subject);
            if (directories.Count == 0)
                return TrainModelResult.Failure($"No hay capturas de {selection.Description}.");
            sources.Add((selection, directories));
        }

        var datasets = new List<string>();
        foreach (var (selection, directories) in sources)
        {
            progress?.Report($"Preparando dataset de {selection.Description}…");
            foreach (var directory in directories)
            {
                var csvPath = Path.Combine(environment.DatasetsDirectory, DatasetFileName(selection.Movement, directory));
                if (!await backend.BuildDatasetAsync(directory, csvPath, selection.Movement.Label, progress))
                    return TrainModelResult.Failure($"No se pudo crear el dataset de {directory}.");
                datasets.Add(csvPath);
            }
        }

        progress?.Report("Entrenando modelo…");
        var result = await backend.TrainModelAsync(environment.ModelPath, datasets, algorithm, progress);
        return result is null
            ? TrainModelResult.Failure("El entrenamiento falló; revisa que el entorno de Python tenga el paquete digital_brain instalado.")
            : TrainModelResult.Success(result, selections.Select(s => s.Movement).DistinctBy(m => m.Label).ToList());
    }

    /// <summary><c>&lt;etiqueta&gt;__&lt;sujeto&gt;.csv</c>, o <c>__anteriores</c> para las capturas sueltas en la carpeta del movimiento.</summary>
    private string DatasetFileName(MovementClass movement, string directory)
    {
        var isMovementFolder = string.Equals(Path.TrimEndingDirectorySeparator(directory), catalog.MovementDirectory(movement),
            StringComparison.OrdinalIgnoreCase);
        var suffix = isMovementFolder ? "anteriores" : Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));
        return $"{movement.Label}__{suffix}.csv";
    }
}

/// <param name="Subject">Sujeto cuyas capturas se usan, o <c>null</c> para todos.</param>
public sealed record TrainingSelection(MovementClass Movement, string? Subject)
{
    public string Description => Subject is null
        ? $"{Movement.DisplayName} (todos los sujetos)"
        : $"{Movement.DisplayName} · {Subject}";
}

public sealed record TrainModelResult(bool Succeeded, TrainingResult? Training, IReadOnlyList<MovementClass> Classes, string? Error)
{
    public static TrainModelResult Success(TrainingResult training, IReadOnlyList<MovementClass> classes) => new(true, training, classes, null);
    public static TrainModelResult Failure(string error) => new(false, null, [], error);
}
