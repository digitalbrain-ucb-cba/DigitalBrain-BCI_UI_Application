using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DigitalBrain.UI.ViewModels;

/// <summary>Estado compartido que muestran la barra lateral y el panel «Estado».</summary>
public partial class SystemStatusViewModel : ObservableObject
{
    [ObservableProperty] private bool _isHeadsetConnected;
    [ObservableProperty] private bool _isHeadsetStreaming;
    [ObservableProperty] private string _headsetDetail = "Desconectado";
    [ObservableProperty] private string _connectedPortName = "";

    [ObservableProperty] private bool _isModelReady;
    [ObservableProperty] private string _modelDetail = "sin entrenar";

    [ObservableProperty] private string _robotDetail = "Wi-Fi";
    [ObservableProperty] private int _recordingCount;
    [ObservableProperty] private string _recordingCountText = "— grabaciones";

    /// <summary>Hay una captura en curso; las pantallas lo usan para no entrenar a la vez.</summary>
    [ObservableProperty] private bool _isCapturing;
    /// <summary>Hay un entrenamiento en curso; las pantallas lo usan para no capturar a la vez.</summary>
    [ObservableProperty] private bool _isTraining;
    /// <summary>La pantalla «Tiempo real» está usando el casco; no se puede capturar a la vez.</summary>
    [ObservableProperty] private bool _isPredicting;
    /// <summary>El osciloscopio está leyendo el casco en vivo; no se puede capturar ni predecir a la vez.</summary>
    [ObservableProperty] private bool _isMonitoring;

    /// <summary>Algún proceso tiene abierto el casco: solo uno puede usarlo a la vez.</summary>
    public bool IsHeadsetBusy => IsCapturing || IsPredicting || IsMonitoring;

    public void RefreshModel(string? modelPath)
    {
        IsModelReady = !string.IsNullOrWhiteSpace(modelPath) && File.Exists(modelPath);
        ModelDetail = IsModelReady ? Path.GetFileName(modelPath)! : "sin entrenar";
    }

    public void UpdateHeadsetStatus(bool hasPort, string? portName = null)
    {
        ConnectedPortName = portName ?? "";

        if (IsHeadsetStreaming || IsHeadsetBusy)
        {
            IsHeadsetConnected = true;
            HeadsetDetail = "Transmitiendo";
        }
        else if (hasPort)
        {
            IsHeadsetConnected = true;
            HeadsetDetail = !string.IsNullOrWhiteSpace(portName) ? $"Conectado ({portName})" : "Conectado";
        }
        else
        {
            IsHeadsetConnected = false;
            HeadsetDetail = "Desconectado";
        }
    }

    partial void OnIsCapturingChanged(bool value) =>
        UpdateHeadsetStatus(!string.IsNullOrWhiteSpace(ConnectedPortName), ConnectedPortName);

    partial void OnIsPredictingChanged(bool value) =>
        UpdateHeadsetStatus(!string.IsNullOrWhiteSpace(ConnectedPortName), ConnectedPortName);

    partial void OnIsMonitoringChanged(bool value) =>
        UpdateHeadsetStatus(!string.IsNullOrWhiteSpace(ConnectedPortName), ConnectedPortName);

    partial void OnIsHeadsetStreamingChanged(bool value) =>
        UpdateHeadsetStatus(!string.IsNullOrWhiteSpace(ConnectedPortName), ConnectedPortName);

    partial void OnRecordingCountChanged(int value) =>
        RecordingCountText = value == 1 ? "1 grabación" : $"{value} grabaciones";
}
