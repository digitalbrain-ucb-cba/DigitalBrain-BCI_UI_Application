namespace DigitalBrain.UI.UseCases.Ports;

/// <summary>Ubicación del backend Python y del intérprete con que se ejecuta.</summary>
public interface IBackendEnvironment
{
    string BackendRoot { get; }
    string PythonExecutable { get; }
    string DataRoot { get; }
    string DatasetsDirectory { get; }
    /// <summary>Carpeta de modelos anterior a guardarlos por sujeto (<c>artifacts/models</c>).</summary>
    string LegacyModelsDirectory { get; }
    /// <summary>Resuelve una ruta relativa a <c>BACK/</c>; deja intactas las absolutas.</summary>
    string Resolve(string path);
}
