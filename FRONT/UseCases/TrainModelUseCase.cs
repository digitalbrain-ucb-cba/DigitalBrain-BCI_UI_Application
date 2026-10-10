using DigitalBrain.UI.UseCases.Ports;
using DigitalBrain.UI.Models;
using DigitalBrain.UI.Services.Backend;

namespace DigitalBrain.UI.UseCases;

/// <summary>
/// Caso de uso «entrenar modelo»: crea un CSV por cada carpeta de clase con capturas
/// (<c>build-dataset</c>) y entrena un único modelo con todos ellos (<c>train</c>).
/// </summary>
public sealed class TrainModelUseCase(
    IBackendBridge backend,
    IRecordingRepository recordings,
    IBackendEnvironment environment)
{
    public const string DefaultAlgorithm = "random_forest";

    public async Task<TrainModelResult> ExecuteAsync(IProgress<string>? progress = null, string algorithm = DefaultAlgorithm)
    {
        var folders = recordings.FoldersWithRecordings();
        if (folders.Count < 2)
            return TrainModelResult.Failure("Se necesitan capturas de al menos dos movimientos para entrenar.");

        var datasets = new List<string>();
        foreach (var (movement, directory) in folders)
        {
            var csvPath = Path.Combine(environment.DatasetsDirectory, $"{movement.Label}.csv");
            progress?.Report($"Preparando dataset de {movement.DisplayName}…");
            if (!await backend.BuildDatasetAsync(directory, csvPath, movement.Label, progress))
                return TrainModelResult.Failure($"No se pudo crear el dataset de {movement.DisplayName}.");
            datasets.Add(csvPath);
        }

        progress?.Report("Entrenando modelo…");
        var result = await backend.TrainModelAsync(environment.ModelPath, datasets, algorithm, progress);
        return result is null
            ? TrainModelResult.Failure("El entrenamiento falló; revisa que el entorno de Python tenga el paquete digital_brain instalado.")
            : TrainModelResult.Success(result, folders.Select(f => f.Movement).ToList());
    }
}

public sealed record TrainModelResult(bool Succeeded, TrainingResult? Training, IReadOnlyList<MovementClass> Classes, string? Error)
{
    public static TrainModelResult Success(TrainingResult training, IReadOnlyList<MovementClass> classes) => new(true, training, classes, null);
    public static TrainModelResult Failure(string error) => new(false, null, [], error);
}
