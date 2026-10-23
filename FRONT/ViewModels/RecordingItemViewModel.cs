using System.Globalization;
using DigitalBrain.UI.Models;

namespace DigitalBrain.UI.ViewModels;

public sealed class RecordingItemViewModel(RecordingSummary recording)
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    public string FileName { get; } = recording.FileName;
    public string FullPath { get; } = recording.FullPath;
    public string MovementName { get; } = recording.Movement?.DisplayName ?? "Sin clase";
    public string SubjectName { get; } = recording.Subject ?? "sin sujeto";
    public string AccentHex { get; } = recording.Movement?.AccentHex ?? "#8A8F98";
    public string DurationText { get; } = $"{recording.DurationSeconds.ToString("0", Es)} s";
    public string ValidCount { get; } = recording.ValidSamples.ToString("N0", Es);
    public string WindowCount { get; } = recording.Windows.ToString("N0", Es);
}
