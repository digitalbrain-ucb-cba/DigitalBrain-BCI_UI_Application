using DigitalBrain.UI.UseCases.Ports;

namespace DigitalBrain.UI.Services.Backend;

/// <summary>
/// Localiza la carpeta <c>BACK/</c> subiendo desde el directorio del ejecutable y elige el intérprete de Python:
/// el configurado por el usuario, el entorno virtual documentado en el README
/// (<c>%LOCALAPPDATA%\DigitalBrain\venv</c>) o <c>python</c> del PATH, en ese orden.
/// </summary>
public sealed class BackendEnvironment : IBackendEnvironment
{
    public BackendEnvironment(ISettingsStore settings)
    {
        BackendRoot = ResolveBackendRoot();
        PythonExecutable = ResolvePython(settings.Load().PythonExecutable);
    }

    public string BackendRoot { get; }
    public string PythonExecutable { get; }
    public string DataRoot => Path.Combine(BackendRoot, "datos");
    public string DatasetsDirectory => Path.Combine(BackendRoot, "artifacts", "datasets");
    public string ModelPath => Path.Combine(BackendRoot, "artifacts", "models", "ModelMotores.pkl");

    public string Resolve(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return BackendRoot;
        path = path.Trim().Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(BackendRoot, path));
    }

    private static string ResolvePython(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        var venvPython = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DigitalBrain", "venv", "Scripts", "python.exe");
        return File.Exists(venvPython) ? venvPython : "python";
    }

    private static string ResolveBackendRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "BACK");
            if (File.Exists(Path.Combine(candidate, "pyproject.toml")))
                return candidate;
        }
        return Directory.GetCurrentDirectory();
    }
}
