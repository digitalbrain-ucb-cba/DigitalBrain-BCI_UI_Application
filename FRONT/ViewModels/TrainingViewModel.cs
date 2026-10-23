using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DigitalBrain.UI.UseCases;
using DigitalBrain.UI.UseCases.Ports;

namespace DigitalBrain.UI.ViewModels;

/// <summary>
/// Pantalla «Entrenar» (paso 2): elige qué movimientos entran en el modelo y, para cada uno,
/// las capturas de qué sujeto (o de todos) se usan; después construye los datasets y entrena.
/// </summary>
public partial class TrainingViewModel : BaseViewModel
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    private readonly TrainModelUseCase _trainModel;
    private readonly IDataCatalog _catalog;
    private readonly IBackendEnvironment _environment;

    public TrainingViewModel(
        TrainModelUseCase trainModel,
        IDataCatalog catalog,
        IBackendEnvironment environment,
        SystemStatusViewModel status)
    {
        Title = "Entrenar";
        _trainModel = trainModel;
        _catalog = catalog;
        _environment = environment;
        Status = status;
        Status.PropertyChanged += OnStatusChanged;
    }

    public SystemStatusViewModel Status { get; }
    public ObservableCollection<TrainingMovementViewModel> Movements { get; } = [];

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(TrainModelCommand))] private bool _isTraining;
    [ObservableProperty] private string _selectionSummary = "";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private bool _isError;
    [ObservableProperty] private string _accuracyText = "—";
    [ObservableProperty] private string _trainedClassesText = "";

    public string ModelFileName => Path.GetFileName(_environment.ModelPath);
    public string AlgorithmText => "Random forest";

    public override void OnNavigatedTo()
    {
        Status.RefreshModel(_environment.ModelPath);
        if (!IsTraining) Reload();
    }

    partial void OnIsTrainingChanged(bool value) => Status.IsTraining = value;

    private void OnStatusChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemStatusViewModel.IsCapturing))
            TrainModelCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void Reload()
    {
        // Conserva lo elegido: la lista se recarga cada vez que se entra en la pantalla.
        var previous = Movements.ToDictionary(m => m.Movement.Label, m => (m.IsSelected, m.SelectedSubject.Name));
        Movements.Clear();
        try
        {
            foreach (var movement in _catalog.GetMovements())
            {
                var row = new TrainingMovementViewModel(
                    movement,
                    _catalog.GetSubjects(movement),
                    subject => _catalog.CountRecordings(movement, subject),
                    OnSelectionChanged);
                if (previous.TryGetValue(movement.Label, out var state))
                    row.Restore(state.IsSelected, state.Name);
                else
                    row.Restore(isSelected: previous.Count == 0, name: null);
                Movements.Add(row);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowMessage($"No se pudieron leer las carpetas de {_environment.DataRoot}: {ex.Message}", isError: true);
        }
        OnSelectionChanged();
    }

    [RelayCommand(CanExecute = nameof(CanTrain))]
    private async Task TrainModelAsync()
    {
        var selections = Movements.Where(m => m.IsReady).Select(m => m.ToSelection()).ToList();
        IsTraining = true;
        ShowMessage("Preparando datasets…");
        try
        {
            var progress = new Progress<string>(line =>
            {
                if (!string.IsNullOrWhiteSpace(line)) ShowMessage(line.Trim());
            });
            var result = await _trainModel.ExecuteAsync(selections, progress);
            if (result.Succeeded && result.Training is { } training)
            {
                AccuracyText = training.Accuracy.ToString("P1", Es);
                TrainedClassesText = string.Join(" · ", selections.Select(s => s.Description));
                ShowMessage($"Modelo guardado en {Path.GetFileName(training.ModelPath)}. " +
                            $"Exactitud {AccuracyText} sobre una división aleatoria de ventanas, no por sesión ni por sujeto.");
                Status.RefreshModel(_environment.ModelPath);
            }
            else
            {
                ShowMessage(result.Error ?? "No se pudo entrenar el modelo.", isError: true);
            }
        }
        catch (Exception ex)
        {
            ShowMessage($"Error al entrenar: {ex.Message}", isError: true);
        }
        finally
        {
            IsTraining = false;
        }
    }

    private bool CanTrain() =>
        !IsTraining && !Status.IsCapturing && Movements.Count(m => m.IsReady) >= 2;

    private void OnSelectionChanged()
    {
        var ready = Movements.Where(m => m.IsReady).ToList();
        var recordings = ready.Sum(m => m.RecordingCount);
        SelectionSummary = ready.Count switch
        {
            0 => "Marca al menos dos movimientos con grabaciones.",
            1 => $"1 movimiento · {recordings} grabaciones. Marca al menos otro para entrenar.",
            var n => $"{n} movimientos · {recordings} grabaciones.",
        };
        TrainModelCommand.NotifyCanExecuteChanged();
    }

    private void ShowMessage(string text, bool isError = false)
    {
        Message = text;
        IsError = isError;
    }
}
