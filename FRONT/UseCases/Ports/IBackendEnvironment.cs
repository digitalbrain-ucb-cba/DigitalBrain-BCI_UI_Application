namespace DigitalBrain.UI.UseCases.Ports;

/// <summary>Ubicación del backend Python y del intérprete con que se ejecuta.</summary>
public interface IBackendEnvironment
{
    string BackendRoot { get; }
    string PythonExecutable { get; }
    string DataRoot { get; }
    string DatasetsDirectory { get; }
    string ModelsDirectory { get; }
    /// <summary>Modelos <c>.pkl</c> de <see cref="ModelsDirectory"/>, el más reciente primero.</summary>
    IReadOnlyList<string> GetModelFiles();
    /// <summary>Resuelve una ruta relativa a <c>BACK/</c>; deja intactas las absolutas.</summary>
    string Resolve(string path);
}
