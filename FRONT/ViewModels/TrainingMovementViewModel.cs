using CommunityToolkit.Mvvm.ComponentModel;
using DigitalBrain.UI.Models;
using DigitalBrain.UI.UseCases;

namespace DigitalBrain.UI.ViewModels;

/// <summary>Opción de la lista de sujetos de un movimiento; <see cref="Name"/> es <c>null</c> para «todos».</summary>
public sealed record SubjectOption(string? Name, string DisplayName)
{
    public static SubjectOption All { get; } = new(null, "Todos los sujetos");
    public override string ToString() => DisplayName;
}

/// <summary>Fila de la pantalla «Entrenar»: si el movimiento entra en el modelo y con las capturas de qué sujeto.</summary>
public partial class TrainingMovementViewModel : ObservableObject
{
    private readonly Func<string?, int> _countRecordings;
    private readonly Action _selectionChanged;

    public TrainingMovementViewModel(
        MovementClass movement,
        IReadOnlyList<string> subjects,
        Func<string?, int> countRecordings,
        Action selectionChanged)
    {
        Movement = movement;
        _countRecordings = countRecordings;
        _selectionChanged = selectionChanged;
        Subjects = [SubjectOption.All, .. subjects.Select(s => new SubjectOption(s, s))];
        _selectedSubject = SubjectOption.All;
        UpdateCount();
    }

    public MovementClass Movement { get; }
    public string Name => Movement.DisplayName;
    public string AccentHex => Movement.AccentHex;
    public IReadOnlyList<SubjectOption> Subjects { get; }

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private SubjectOption _selectedSubject;
    [ObservableProperty] private int _recordingCount;
    [ObservableProperty] private string _recordingCountText = "";

    public bool HasRecordings => RecordingCount > 0;
    public bool IsReady => IsSelected && HasRecordings;
    public TrainingSelection ToSelection() => new(Movement, SelectedSubject.Name);

    /// <summary>Vuelve a elegir el sujeto <paramref name="name"/> si sigue existiendo (al recargar la lista).</summary>
    public void Restore(bool isSelected, string? name)
    {
        SelectedSubject = Subjects.FirstOrDefault(s => s.Name == name) ?? SubjectOption.All;
        IsSelected = isSelected && HasRecordings;
    }

    partial void OnIsSelectedChanged(bool value) => _selectionChanged();

    partial void OnSelectedSubjectChanged(SubjectOption value)
    {
        UpdateCount();
        _selectionChanged();
    }

    partial void OnRecordingCountChanged(int value) => OnPropertyChanged(nameof(HasRecordings));

    private void UpdateCount()
    {
        RecordingCount = _countRecordings(SelectedSubject?.Name);
        RecordingCountText = RecordingCount switch
        {
            0 => "sin grabaciones",
            1 => "1 grabación",
            var n => $"{n} grabaciones",
        };
    }
}
