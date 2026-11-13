using DigitalBrain.UI.UseCases.Ports;
using DigitalBrain.UI.Models;
using DigitalBrain.UI.Services.Backend;

namespace DigitalBrain.UI.UseCases;

/// <summary>
/// Caso de uso «entrenar modelo»: crea un CSV por cada movimiento elegido del sujeto con <c>build-dataset</c>,
/// entrena un único modelo con todos ellos (<c>train</c>) y lo guarda en la carpeta de modelos del sujeto.
/// </summary>
public sealed class TrainModelUseCase(
    IBackendBridge backend,
    IDataCatalog catalog,
    IRecordingRepository recordings,
    IModelStore models,
    IBackendEnvironment environment)
{
    /// <summary>La división estratificada de <c>train</c> necesita al menos dos ventanas por clase.</summary>
    public const int MinimumWindowsPerMovement = 2;

    public async Task<TrainModelResult> ExecuteAsync(
        string subject,
        IReadOnlyList<MovementClass> movements,
        string modelName,
        string algorithm,
        IProgress<string>? progress = null)
    {
        movements = movements.DistinctBy(m => m.Label).ToList();
        if (movements.Count < 2)
            return TrainModelResult.Failure("Elige al menos dos movimientos para entrenar.");

        foreach (var movement in movements)
        {
            if (catalog.CountRecordings(subject, movement) == 0)
                return TrainModelResult.Failure($"{subject} no tiene grabaciones de {movement.DisplayName}.");

            // Una grabación corta o saturada genera un CSV vacío sin error, y luego train falla con un mensaje poco claro:
            // se comprueba antes con las mismas reglas de ventaneo que build-dataset.
            var windows = 0;
            foreach (var file in Directory.EnumerateFiles(catalog.MovementDirectory(subject, movement), "*.txt"))
                windows += (await recordings.AnalyzeAsync(file))?.Windows ?? 0;
            if (windows < MinimumWindowsPerMovement)
                return TrainModelResult.Failure(
                    $"Las grabaciones de {movement.DisplayName} de {subject} solo dan {windows} ventanas: cada archivo necesita al menos " +
                    $"{AcquisitionParameters.WindowSize} muestras válidas ({AcquisitionParameters.WindowSize / AcquisitionParameters.SampleRateHz:0.#} s sin saturar). " +
                    "Graba más tiempo.");
        }

        var datasets = new List<string>();
        foreach (var movement in movements)
        {
            progress?.Report($"Preparando dataset de {movement.DisplayName}…");
            var csvPath = Path.Combine(environment.DatasetsDirectory, $"{subject}__{movement.Label}.csv");
            if (!await backend.BuildDatasetAsync(catalog.MovementDirectory(subject, movement), csvPath, movement.Label, progress))
                return TrainModelResult.Failure($"No se pudo crear el dataset de {movement.DisplayName}: {backend.LastError ?? "error desconocido"}");
            datasets.Add(csvPath);
        }

        progress?.Report("Entrenando modelo…");
        var modelPath = models.ModelPath(subject, modelName);
        var result = await backend.TrainModelAsync(modelPath, datasets, algorithm, progress);
        if (result is null)
        {
            return TrainModelResult.Failure(backend.LastError is { } error
                ? $"El entrenamiento falló: {error}"
                : "El entrenamiento falló; revisa que el entorno de Python tenga el paquete digital_brain instalado.");
        }

        models.SaveInfo(new ModelInfo(modelPath, subject, algorithm, result.Accuracy, result.CompletedAt, movements.Select(m => m.Label).ToList()));
        return TrainModelResult.Success(result, movements);
    }
}

public sealed record TrainModelResult(bool Succeeded, TrainingResult? Training, IReadOnlyList<MovementClass> Classes, string? Error)
{
    public static TrainModelResult Success(TrainingResult training, IReadOnlyList<MovementClass> classes) => new(true, training, classes, null);
    public static TrainModelResult Failure(string error) => new(false, null, [], error);
}
