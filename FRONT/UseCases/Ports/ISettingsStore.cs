namespace DigitalBrain.UI.UseCases.Ports;

public sealed record UserSettings(string? ExecutablePath = null, string Theme = "Light", string? PythonExecutable = null);

/// <summary>Preferencias del usuario que se conservan entre ejecuciones.</summary>
public interface ISettingsStore
{
    UserSettings Load();
    void Save(UserSettings settings);
}
