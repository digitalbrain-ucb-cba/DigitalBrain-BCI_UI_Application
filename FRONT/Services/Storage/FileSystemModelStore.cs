using System.Diagnostics;
using System.Text.Json;
using DigitalBrain.UI.Models;
using DigitalBrain.UI.UseCases.Ports;

namespace DigitalBrain.UI.Services.Storage;

/// <summary>
/// Modelos <c>.pkl</c> en la carpeta de cada sujeto y tipo. Los datos de su entrenamiento van en un <c>.json</c> con el mismo nombre;
/// los modelos sin él (los anteriores a este cambio o los copiados a mano) se muestran con la fecha del archivo.
/// </summary>
public sealed class FileSystemModelStore(IDataCatalog catalog, IBackendEnvironment environment) : IModelStore
{
    private const string ModelExtension = ".pkl";
    private const string InfoExtension = ".json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public IReadOnlyList<ModelInfo> GetModels(string subject, ModelType type) => Read(catalog.ModelsDirectory(subject, type), subject, type);

    public IReadOnlyList<ModelInfo> GetAllModels() =>
        catalog.GetSubjects()
            .SelectMany(subject => ModelType.All.SelectMany(type => GetModels(subject, type)))
            .Concat(Read(environment.LegacyModelsDirectory, subject: null, type: null))
            .OrderByDescending(model => model.TrainedAt)
            .ToList();

    public string ModelPath(string subject, ModelType type, string name) =>
        Path.Combine(catalog.ModelsDirectory(subject, type), name + ModelExtension);

    public void SaveInfo(ModelInfo model)
    {
        try
        {
            var info = new StoredInfo(model.AlgorithmId, model.Accuracy, model.TrainedAt, model.Movements);
            File.WriteAllText(InfoPath(model.FilePath), JsonSerializer.Serialize(info, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Sin estos datos el modelo sigue sirviendo; solo se muestra con menos detalle.
            Debug.WriteLine($"No se guardaron los datos de {model.FileName}: {ex.Message}");
        }
    }

    public void Delete(ModelInfo model)
    {
        File.Delete(model.FilePath);
        if (File.Exists(InfoPath(model.FilePath)))
            File.Delete(InfoPath(model.FilePath));
    }

    private static IReadOnlyList<ModelInfo> Read(string directory, string? subject, ModelType? type)
    {
        if (!Directory.Exists(directory)) return [];
        return Directory.EnumerateFiles(directory, "*" + ModelExtension)
            .Select(path => ReadOne(path, subject, type))
            .OrderByDescending(model => model.TrainedAt)
            .ToList();
    }

    private static ModelInfo ReadOne(string path, string? subject, ModelType? type)
    {
        var info = ReadInfo(path);
        var name = Path.GetFileNameWithoutExtension(path);
        // Sin datos guardados, el algoritmo se deduce del nombre automático «…_<algoritmo>».
        var algorithm = info?.AlgorithmId
                        ?? TrainingAlgorithm.All.FirstOrDefault(a => name.EndsWith("_" + a.Id, StringComparison.OrdinalIgnoreCase))?.Id;
        return new ModelInfo(path, subject, type, algorithm, info?.Accuracy, info?.TrainedAt ?? File.GetLastWriteTime(path), info?.Movements ?? []);
    }

    private static StoredInfo? ReadInfo(string modelPath)
    {
        var infoPath = InfoPath(modelPath);
        if (!File.Exists(infoPath)) return null;
        try
        {
            return JsonSerializer.Deserialize<StoredInfo>(File.ReadAllText(infoPath));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string InfoPath(string modelPath) => Path.ChangeExtension(modelPath, InfoExtension);

    private sealed record StoredInfo(string? AlgorithmId, double? Accuracy, DateTime TrainedAt, IReadOnlyList<string>? Movements);
}
