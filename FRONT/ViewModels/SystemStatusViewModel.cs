using CommunityToolkit.Mvvm.ComponentModel;

namespace DigitalBrain.UI.ViewModels;

/// <summary>Estado compartido que muestran la barra lateral y el panel «Estado».</summary>
public partial class SystemStatusViewModel : ObservableObject
{
    [ObservableProperty] private bool _isHeadsetStreaming;
    [ObservableProperty] private string _headsetDetail = "USB · 4 canales";
    [ObservableProperty] private bool _isModelReady;
    [ObservableProperty] private string _modelDetail = "sin entrenar";
    [ObservableProperty] private string _robotDetail = "Wi-Fi";
    [ObservableProperty] private int _recordingCount;
    [ObservableProperty] private string _recordingCountText = "— grabaciones";

    partial void OnRecordingCountChanged(int value) =>
        RecordingCountText = value == 1 ? "1 grabación" : $"{value} grabaciones";
}
