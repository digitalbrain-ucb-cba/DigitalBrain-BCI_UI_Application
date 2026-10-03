using DigitalBrain.UI.Models;

namespace DigitalBrain.UI.Services.Hardware;

public interface ICaptureService
{
    bool IsCapturing { get; }

    event EventHandler<SignalSample>? SampleReceived;
    event EventHandler<string>? StatusChanged;
    event EventHandler<string>? CaptureCompleted;

    Task StartCaptureAsync(
        string executablePath,
        string outputDirectory,
        SessionMetadata metadata,
        CancellationToken cancellationToken = default
    );

    Task StopCaptureAsync();
}
