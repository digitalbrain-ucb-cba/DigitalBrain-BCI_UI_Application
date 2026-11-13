using System.Windows.Threading;
using Microsoft.Win32;
using DigitalBrain.UI.ViewModels;

namespace DigitalBrain.UI.Services.Hardware;

/// <summary>
/// Monitorea activamente los puertos serie/USB (COM) registrados en Windows
/// para detectar si el adaptador o casco EEG está enchufado físicamente a la computadora.
/// </summary>
public sealed class WindowsHeadsetDetector : IHeadsetDetector, IDisposable
{
    private readonly SystemStatusViewModel _status;
    private readonly DispatcherTimer _timer;
    private bool _isConnected;
    private string? _portName;

    public WindowsHeadsetDetector(SystemStatusViewModel status)
    {
        _status = status;
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1.5)
        };
        _timer.Tick += (_, _) => Poll();
    }

    public bool IsConnected => _isConnected;
    public string? PortName => _portName;

    public event EventHandler? StatusChanged;

    public void StartMonitoring()
    {
        Poll();
        _timer.Start();
    }

    public void StopMonitoring()
    {
        _timer.Stop();
    }

    public (bool IsConnected, string? PortName) CheckConnection()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
            if (key is not null)
            {
                var names = key.GetValueNames();
                foreach (var name in names)
                {
                    if (key.GetValue(name) is string port && !string.IsNullOrWhiteSpace(port))
                    {
                        return (true, port);
                    }
                }
            }
        }
        catch
        {
            // Ignorar en caso de restricciones de permisos del sistema
        }

        return (false, null);
    }

    public void Poll()
    {
        var (connected, port) = CheckConnection();
        var changed = connected != _isConnected || port != _portName;
        _isConnected = connected;
        _portName = port;

        _status.UpdateHeadsetStatus(connected, port);

        if (changed)
        {
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        _timer.Stop();
    }
}
