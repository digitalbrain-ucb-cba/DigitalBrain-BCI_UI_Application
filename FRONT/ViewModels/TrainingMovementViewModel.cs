using CommunityToolkit.Mvvm.ComponentModel;
using DigitalBrain.UI.Models;

namespace DigitalBrain.UI.ViewModels;

/// <summary>Fila de la pantalla «Entrenar»: si el movimiento entra en el modelo y cuántas grabaciones tiene el sujeto elegido.</summary>
public partial class TrainingMovementViewModel : ObservableObject
{
    private readonly Action _selectionChanged;

    public TrainingMovementViewModel(MovementClass movement, int recordingCount, bool isSelected, Action selectionChanged)
    {
        Movement = movement;
        _selectionChanged = selectionChanged;
        RecordingCount = recordingCount;
        RecordingCountText = recordingCount switch
        {
            0 => "sin grabaciones",
            1 => "1 grabación",
            var n => $"{n} grabaciones",
        };
        _isSelected = isSelected && HasRecordings;
    }

    public MovementClass Movement { get; }
    public string Name => Movement.DisplayName;
    public string AccentHex => Movement.AccentHex;
    public int RecordingCount { get; }
    public string RecordingCountText { get; }
    public bool HasRecordings => RecordingCount > 0;
    public bool IsReady => IsSelected && HasRecordings;

    [ObservableProperty] private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => _selectionChanged();
}
