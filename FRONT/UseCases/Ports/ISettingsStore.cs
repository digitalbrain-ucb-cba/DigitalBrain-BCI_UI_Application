namespace DigitalBrain.UI.UseCases.Ports;

public sealed record UserSettings(
    string? ExecutablePath = null,
    string Theme = "Light",
    string? PythonExecutable = null,
    double SampleRateHz = 125.0);

/// <summary>Preferencias del usuario que se conservan entre ejecuciones.</summary>
public interface ISettingsStore
{
    UserSettings Load();
    void Save(UserSettings settings);
}
