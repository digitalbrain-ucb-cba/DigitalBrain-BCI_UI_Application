namespace DigitalBrain.UI.Services.Hardware;

/// <summary>
/// Servicio para monitorear la conexión física por USB / Serial del casco EEG.
/// </summary>
public interface IHeadsetDetector
{
    bool IsConnected { get; }
    string? PortName { get; }
    event EventHandler? StatusChanged;

    void StartMonitoring();
    void StopMonitoring();
    void Poll();
    (bool IsConnected, string? PortName) CheckConnection();
}
