using System.Text.Json.Serialization;

namespace DigitalBrain.UI.UseCases.Ports;

public sealed record UserSettings(
    string? ExecutablePath = null,
    string Theme = "Light",
    string? PythonExecutable = null,
    double SampleRateHz = 125.0)
{
    /// <summary>Ruta del equipo de pruebas donde está instalado el capturador.</summary>
    public const string DefaultExecutablePath = @"D:\Documentos\DIGITAL BRAIN\SOFTWARE\LecturaEEG\ConDumpAuto\ConDumpAuto.exe";

    /// <summary>La ruta guardada por el usuario o, si no hay ninguna, la del equipo de pruebas.</summary>
    [JsonIgnore]
    public string CaptureExecutablePath => string.IsNullOrWhiteSpace(ExecutablePath) ? DefaultExecutablePath : ExecutablePath;
}

/// <summary>Preferencias del usuario que se conservan entre ejecuciones.</summary>
public interface ISettingsStore
{
    UserSettings Load();
    void Save(UserSettings settings);
}
