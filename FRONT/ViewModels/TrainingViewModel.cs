using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DigitalBrain.UI.Models;
using DigitalBrain.UI.Services.Dialogs;
using DigitalBrain.UI.UseCases;
using DigitalBrain.UI.UseCases.Ports;

namespace DigitalBrain.UI.ViewModels;

/// <summary>
/// Pantalla «Entrenar» (paso 2): elige el sujeto, qué movimientos suyos entran en el modelo, con qué algoritmo y con qué
/// nombre se guarda en su carpeta de modelos; también lista y permite borrar los modelos que ya tiene.
/// </summary>
public partial class TrainingViewModel : BaseViewModel
{
    private const int MaxModelNameLength = 100;
    private const string ModelExtension = ".pkl";
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    private readonly TrainModelUseCase _trainModel;
    private readonly IDataCatalog _catalog;
    private readonly IModelStore _models;
    private readonly IBackendEnvironment _environment;
    private readonly IDialogService _dialogs;
    /// <summary>El usuario escribió su propio nombre: elegir otro sujeto o algoritmo ya no lo cambia.</summary>
    private bool _isModelNameCustom;
    /// <summary>Sujeto de las filas actuales: lo marcado solo se conserva al recargar el mismo sujeto.</summary>
    private string? _movementsSubject;

    public TrainingViewModel(
        TrainModelUseCase trainModel,
        IDataCatalog catalog,
        IModelStore models,
        IBackendEnvironment environment,
        IDialogService dialogs,
        SystemStatusViewModel status)
    {
        Title = "Entrenar";
        _trainModel = trainModel;
        _catalog = catalog;
        _models = models;
        _environment = environment;
        _dialogs = dialogs;
        Status = status;
        Status.PropertyChanged += OnStatusChanged;
        UpdateSuggestedModelName();
    }

    public SystemStatusViewModel Status { get; }
    public ObservableCollection<string> Subjects { get; } = [];
    public ObservableCollection<TrainingMovementViewModel> Movements { get; } = [];
    public ObservableCollection<ModelItemViewModel> SubjectModels { get; } = [];
    public IReadOnlyList<TrainingAlgorithm> Algorithms => TrainingAlgorithm.All;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(SubjectModelsTitle))] private string? _selectedSubject;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TrainModelCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteModelCommand))]
    private bool _isTraining;
    [ObservableProperty] private string _selectionSummary = "";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private bool _isError;
    [ObservableProperty] private string _accuracyText = "—";
    [ObservableProperty] private string _trainedClassesText = "";

    [ObservableProperty] private TrainingAlgorithm _selectedAlgorithm = TrainingAlgorithm.All[0];
    [ObservableProperty] private string _modelName = "";
    [ObservableProperty] private string _suggestedModelName = "";
    [ObservableProperty] private string _modelNameWarning = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(EditModelNameText))] private bool _isEditingModelName;

    public string EditModelNameText => IsEditingModelName ? "Listo" : "Editar";
    public string SubjectModelsTitle => SelectedSubject is null ? "Modelos" : $"Modelos de {SelectedSubject}";
    public bool HasSubjectModels => SubjectModels.Count > 0;

    public override void OnNavigatedTo()
    {
        if (!IsTraining) Reload();
    }

    partial void OnIsTrainingChanged(bool value) => Status.IsTraining = value;

    partial void OnSelectedSubjectChanged(string? value)
    {
        ReloadMovements();
        ReloadModels();
        UpdateSuggestedModelName();
    }

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

    /// <summary>Vuelve a leer sujetos, movimientos y modelos del disco, conservando lo elegido.</summary>
    [RelayCommand]
    private void Reload()
    {
        var keep = SelectedSubject;
        Subjects.Clear();
        try
        {
            foreach (var subject in _catalog.GetSubjects())
                Subjects.Add(subject);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowMessage($"No se pudieron leer las carpetas de {_environment.DataRoot}: {ex.Message}", isError: true);
        }
        var subjectToSelect = Subjects.FirstOrDefault(s => string.Equals(s, keep, StringComparison.CurrentCultureIgnoreCase))
                              ?? Subjects.FirstOrDefault();
        // Forzar la recarga aunque el sujeto no cambie: sus grabaciones o modelos pudieron cambiar fuera.
        SelectedSubject = null;
        SelectedSubject = subjectToSelect;
        Status.RefreshModel(_models.GetAllModels().FirstOrDefault()?.FilePath);
    }

    [RelayCommand(CanExecute = nameof(CanTrain))]
    private async Task TrainModelAsync()
    {
        if (SelectedSubject is not { } subject) return;
        var modelName = EffectiveModelName();
        if (CatalogNames.Validate(modelName, "modelo", MaxModelNameLength) is { } nameError)
        {
            ShowMessage(nameError, isError: true);
            return;
        }

        var movements = Movements.Where(m => m.IsReady).Select(m => m.Movement).ToList();
        var algorithm = SelectedAlgorithm;
        IsTraining = true;
        ShowMessage("Preparando datasets…");
        try
        {
            var progress = new Progress<string>(line =>
            {
                if (!string.IsNullOrWhiteSpace(line)) ShowMessage(line.Trim());
            });
            var result = await _trainModel.ExecuteAsync(subject, movements, modelName, algorithm.Id, progress);
            if (result.Succeeded && result.Training is { } training)
            {
                AccuracyText = training.Accuracy.ToString("P1", Es);
                TrainedClassesText = $"{subject}: {string.Join(" · ", movements.Select(m => m.DisplayName))}";
                ShowMessage($"Modelo {algorithm.DisplayName} guardado en {Path.GetFileName(training.ModelPath)}. " +
                            $"Exactitud {AccuracyText} sobre una división aleatoria de ventanas, no por sesión ni por sujeto.");
                Status.RefreshModel(training.ModelPath);
                ReloadModels();
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
        !IsTraining && !Status.IsCapturing && SelectedSubject is not null && Movements.Count(m => m.IsReady) >= 2;

    [RelayCommand(CanExecute = nameof(CanDeleteModel))]
    private void DeleteModel(ModelItemViewModel? item)
    {
        if (item is null) return;
        if (!_dialogs.Confirm("Eliminar modelo", $"¿Eliminar {item.Model.FileName} de {SelectedSubject}? No se puede deshacer."))
            return;
        try
        {
            _models.Delete(item.Model);
            ShowMessage($"Se eliminó {item.Model.FileName}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowMessage($"No se pudo eliminar {item.Model.FileName}: {ex.Message}", isError: true);
        }
        ReloadModels();
        UpdateModelNameWarning();
        Status.RefreshModel(_models.GetAllModels().FirstOrDefault()?.FilePath);
    }

    private bool CanDeleteModel(ModelItemViewModel? item) => item is not null && !IsTraining;

    private void ReloadMovements()
    {
        // Al recargar el mismo sujeto se conserva lo marcado; con otro sujeto se marcan todos los que tienen grabaciones,
        // porque lo que no se pudo marcar en el anterior por no tener grabaciones no es una elección del usuario.
        var selected = SelectedSubject is not null && SelectedSubject == _movementsSubject
            ? Movements.Where(m => m.IsSelected).Select(m => m.Movement.Label).ToHashSet()
            : null;
        _movementsSubject = SelectedSubject;
        Movements.Clear();
        if (SelectedSubject is { } subject)
        {
            try
            {
                foreach (var movement in _catalog.GetMovements())
                {
                    Movements.Add(new TrainingMovementViewModel(
                        movement, _catalog.CountRecordings(subject, movement),
                        isSelected: selected?.Contains(movement.Label) ?? true, OnSelectionChanged));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ShowMessage($"No se pudieron leer las grabaciones de {subject}: {ex.Message}", isError: true);
            }
        }
        OnSelectionChanged();
    }

    private void ReloadModels()
    {
        SubjectModels.Clear();
        if (SelectedSubject is { } subject)
        {
            foreach (var model in _models.GetModels(subject))
                SubjectModels.Add(new ModelItemViewModel(model));
        }
        OnPropertyChanged(nameof(HasSubjectModels));
    }

    private void OnSelectionChanged()
    {
        var ready = Movements.Where(m => m.IsReady).ToList();
        var recordings = ready.Sum(m => m.RecordingCount);
        SelectionSummary = SelectedSubject is null
            ? "Añade un sujeto en Capturar para poder entrenar."
            : ready.Count switch
            {
                0 => "Marca al menos dos movimientos con grabaciones.",
                1 => $"1 movimiento · {recordings} grabaciones. Marca al menos otro para entrenar.",
                var n => $"{n} movimientos · {recordings} grabaciones.",
            };
        TrainModelCommand.NotifyCanExecuteChanged();
    }

    private void UpdateSuggestedModelName()
    {
        SuggestedModelName = $"{SelectedSubject ?? "modelo"}_{SelectedAlgorithm.Id}";
        if (!_isModelNameCustom) ModelName = SuggestedModelName;
        UpdateModelNameWarning();
    }

    /// <summary>El nombre escrito sin la extensión o, si está vacío, el sugerido.</summary>
    private string EffectiveModelName()
    {
        var name = CatalogNames.Normalize(ModelName);
        if (name.EndsWith(ModelExtension, StringComparison.OrdinalIgnoreCase))
            name = name[..^ModelExtension.Length].TrimEnd();
        return name.Length > 0 ? name : SuggestedModelName;
    }

    private void UpdateModelNameWarning()
    {
        var name = EffectiveModelName();
        ModelNameWarning = CatalogNames.Validate(name, "modelo", MaxModelNameLength)
                           ?? (SelectedSubject is { } subject && File.Exists(_models.ModelPath(subject, name))
                               ? $"Ya existe {name}{ModelExtension}: al entrenar se reemplazará."
                               : "");
    }

    private void ShowMessage(string text, bool isError = false)
    {
        Message = text;
        IsError = isError;
    }
}
