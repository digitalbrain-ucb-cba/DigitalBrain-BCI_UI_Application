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
    /// <summary>Hay una captura en curso; las pantallas lo usan para no entrenar a la vez.</summary>
    [ObservableProperty] private bool _isCapturing;
    /// <summary>Hay un entrenamiento en curso; las pantallas lo usan para no capturar a la vez.</summary>
    [ObservableProperty] private bool _isTraining;

    public void RefreshModel(string modelPath)
    {
        IsModelReady = File.Exists(modelPath);
        ModelDetail = IsModelReady ? Path.GetFileName(modelPath) : "sin entrenar";
    }

    partial void OnRecordingCountChanged(int value) =>
        RecordingCountText = value == 1 ? "1 grabación" : $"{value} grabaciones";
}
