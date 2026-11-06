using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DigitalBrain.UI.Models;
using DigitalBrain.UI.UseCases;
using DigitalBrain.UI.UseCases.Ports;

namespace DigitalBrain.UI.ViewModels;

/// <summary>
/// Pantalla «Entrenar» (paso 2): elige qué movimientos entran en el modelo y, para cada uno,
/// las capturas de qué sujeto (o de todos) se usan, con qué algoritmo y con qué nombre se guarda el modelo;
/// después construye los datasets y entrena.
/// </summary>
public partial class TrainingViewModel : BaseViewModel
{
    private const string ModelExtension = ".pkl";
    private const int MaxModelNameLength = 100;
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    private readonly TrainModelUseCase _trainModel;
    private readonly IDataCatalog _catalog;
    private readonly IBackendEnvironment _environment;
    /// <summary>El usuario escribió su propio nombre: elegir otro sujeto o algoritmo ya no lo cambia.</summary>
    private bool _isModelNameCustom;

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
        UpdateSuggestedModelName();
    }

    public SystemStatusViewModel Status { get; }
    public ObservableCollection<TrainingMovementViewModel> Movements { get; } = [];

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(TrainModelCommand))] private bool _isTraining;
    [ObservableProperty] private string _selectionSummary = "";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private bool _isError;
    [ObservableProperty] private string _accuracyText = "—";
    [ObservableProperty] private string _trainedClassesText = "";

    public IReadOnlyList<TrainingAlgorithm> Algorithms => TrainingAlgorithm.All;

    [ObservableProperty] private TrainingAlgorithm _selectedAlgorithm = TrainingAlgorithm.All[0];
    [ObservableProperty] private string _modelName = "";
    [ObservableProperty] private string _suggestedModelName = "";
    [ObservableProperty] private string _modelNameWarning = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(EditModelNameText))] private bool _isEditingModelName;

    public string EditModelNameText => IsEditingModelName ? "Listo" : "Editar";

    public override void OnNavigatedTo()
    {
        Status.RefreshModel(_environment.GetModelFiles().FirstOrDefault());
        if (!IsTraining) Reload();
        UpdateModelNameWarning();
    }

    partial void OnIsTrainingChanged(bool value) => Status.IsTraining = value;

    partial void OnSelectedAlgorithmChanged(TrainingAlgorithm value) => UpdateSuggestedModelName();

    partial void OnModelNameChanged(string value)
    {
        _isModelNameCustom = !string.IsNullOrWhiteSpace(value) && value != SuggestedModelName;
        UpdateModelNameWarning();
    }

    private void OnStatusChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemStatusViewModel.IsCapturing))
            TrainModelCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ToggleEditModelName()
    {
        // Dejarlo vacío devuelve el nombre automático.
        if (IsEditingModelName && string.IsNullOrWhiteSpace(ModelName))
            ModelName = SuggestedModelName;
        IsEditingModelName = !IsEditingModelName;
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
        var modelName = EffectiveModelName();
        if (CatalogNames.Validate(modelName, "modelo", MaxModelNameLength) is { } nameError)
        {
            ShowMessage(nameError, isError: true);
            return;
        }

        var selections = Movements.Where(m => m.IsReady).Select(m => m.ToSelection()).ToList();
        var algorithm = SelectedAlgorithm;
        IsTraining = true;
        ShowMessage("Preparando datasets…");
        try
        {
            var progress = new Progress<string>(line =>
            {
                if (!string.IsNullOrWhiteSpace(line)) ShowMessage(line.Trim());
            });
            var result = await _trainModel.ExecuteAsync(selections, ModelPathFor(modelName), algorithm.Id, progress);
            if (result.Succeeded && result.Training is { } training)
            {
                AccuracyText = training.Accuracy.ToString("P1", Es);
                TrainedClassesText = string.Join(" · ", selections.Select(s => s.Description));
                ShowMessage($"Modelo {algorithm.DisplayName} guardado en {Path.GetFileName(training.ModelPath)}. " +
                            $"Exactitud {AccuracyText} sobre una división aleatoria de ventanas, no por sesión ni por sujeto.");
                Status.RefreshModel(training.ModelPath);
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
            UpdateModelNameWarning();
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
        UpdateSuggestedModelName();
    }

    private void UpdateSuggestedModelName()
    {
        var subjects = Movements.Where(m => m.IsReady).Select(m => m.SelectedSubject.Name).Distinct().ToList();
        var subject = subjects.Count switch
        {
            0 => "modelo",
            1 => subjects[0] ?? "todos",
            _ => subjects.Select(ModelType.Of).Distinct().ToList() is [{ } type] ? $"{type.Prefix}_varios" : "varios",
        };
        SuggestedModelName = $"{subject}_{SelectedAlgorithm.Id}";
        if (!_isModelNameCustom) ModelName = SuggestedModelName;
    }

    /// <summary>El nombre escrito sin la extensión o, si está vacío, el sugerido.</summary>
    private string EffectiveModelName()
    {
        var name = CatalogNames.Normalize(ModelName);
        if (name.EndsWith(ModelExtension, StringComparison.OrdinalIgnoreCase))
            name = name[..^ModelExtension.Length].TrimEnd();
        return name.Length > 0 ? name : SuggestedModelName;
    }

    private string ModelPathFor(string name) => Path.Combine(_environment.ModelsDirectory, name + ModelExtension);

    private void UpdateModelNameWarning()
    {
        var name = EffectiveModelName();
        ModelNameWarning = CatalogNames.Validate(name, "modelo", MaxModelNameLength)
                           ?? (File.Exists(ModelPathFor(name)) ? $"Ya existe {name}{ModelExtension}: al entrenar se reemplazará." : "");
    }

    private void ShowMessage(string text, bool isError = false)
    {
        Message = text;
        IsError = isError;
    }
}
