using DigitalBrain.UI.Models;

namespace DigitalBrain.UI.Services.Backend;

public interface IBackendBridge
{
    /// <summary>Última línea de error de Python (p. ej. «ValueError: …») de la orden que falló.</summary>
    string? LastError { get; }

    Task<bool> ValidatePythonEnvironmentAsync();
    
    Task<bool> BuildDatasetAsync(
        string inputDirectory,
        string outputCsvPath,
        string label,
        IProgress<string>? progress = null
    );

    Task<TrainingResult?> TrainModelAsync(
        string outputModelPath,
        IEnumerable<string> datasetPaths,
        string classifierType = "random_forest",
        IProgress<string>? progress = null
    );
}
