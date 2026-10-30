using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DigitalBrain.UI.Models;
using DigitalBrain.UI.UseCases;
using DigitalBrain.UI.UseCases.Ports;

namespace DigitalBrain.UI.ViewModels;

/// <summary>
/// Pantalla «Entrenar» (paso 2): elige qué movimientos entran en el modelo y, para cada uno,
/// las capturas de qué sujeto (o de todos) se usan; selecciona el algoritmo y genera un modelo
/// identificado con el nombre de la persona y el algoritmo sin sobrescribir archivos anteriores.
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

        AvailableAlgorithms = TrainingAlgorithm.All;
        _selectedAlgorithm = TrainingAlgorithm.Default;
        _personName = "Sujeto1";
        ValidatePersonName();
    }

    public SystemStatusViewModel Status { get; }
    public ObservableCollection<TrainingMovementViewModel> Movements { get; } = [];
    public IReadOnlyList<TrainingAlgorithm> AvailableAlgorithms { get; }

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(TrainModelCommand))] private bool _isTraining;
    [ObservableProperty] private string _selectionSummary = "";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private bool _isError;
    [ObservableProperty] private string _accuracyText = "—";
    [ObservableProperty] private string _trainedClassesText = "";
    [ObservableProperty] private bool _isModelTrained;
    [ObservableProperty] private string _lastTrainedModelFileName = "";

    [ObservableProperty] private string _personName = "";
    [ObservableProperty] private string _personNameWarning = "";
    [ObservableProperty] private TrainingAlgorithm _selectedAlgorithm;

    public string AlgorithmText => SelectedAlgorithm?.DisplayName ?? "Random Forest";
    public string ModelFileName => !string.IsNullOrEmpty(LastTrainedModelFileName)
        ? LastTrainedModelFileName
        : TargetModelFileName;

    public string TargetModelFileName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(PersonName) || !string.IsNullOrEmpty(PersonNameWarning))
                return "—";
            return Path.GetFileName(ResolveTargetModelPath());
        }
    }

    public override void OnNavigatedTo()
    {
        RefreshStatus();
        if (!IsTraining) Reload();
    }

    partial void OnIsTrainingChanged(bool value) => Status.IsTraining = value;

    partial void OnPersonNameChanged(string value)
    {
        ValidatePersonName();
        OnPropertyChanged(nameof(TargetModelFileName));
        OnPropertyChanged(nameof(ModelFileName));
        TrainModelCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedAlgorithmChanged(TrainingAlgorithm value)
    {
        OnPropertyChanged(nameof(AlgorithmText));
        OnPropertyChanged(nameof(TargetModelFileName));
        OnPropertyChanged(nameof(ModelFileName));
    }

    private void OnStatusChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemStatusViewModel.IsCapturing))
            TrainModelCommand.NotifyCanExecuteChanged();
    }

    private void ValidatePersonName()
    {
        if (string.IsNullOrWhiteSpace(PersonName))
        {
            PersonNameWarning = "Escribe el nombre de la persona para nombrar el modelo.";
            return;
        }

        var trimmed = PersonName.Trim();
        if (CatalogNames.Validate(trimmed, "persona") is { } error)
        {
            PersonNameWarning = error;
            return;
        }

        PersonNameWarning = "";
    }

    /// <summary>
    /// Resuelve la ruta de guardado sin sobrescribir modelos previos:
    /// añade un sufijo numérico (_1, _2, ...) si ya existe un archivo con ese nombre.
    /// </summary>
    public string ResolveTargetModelPath()
    {
        var rawName = string.IsNullOrWhiteSpace(PersonName) ? "sujeto" : PersonName.Trim();
        var sanitizedPerson = SanitizeFileName(rawName);
        var algorithmId = SelectedAlgorithm?.Id ?? TrainingAlgorithm.Default.Id;
        var baseName = $"{sanitizedPerson}_{algorithmId}";

        var dir = _environment.ModelsDirectory;
        Directory.CreateDirectory(dir);

        var directPath = Path.Combine(dir, $"{baseName}.pkl");
        if (!File.Exists(directPath))
            return directPath;

        int index = 1;
        while (true)
        {
            var candidate = Path.Combine(dir, $"{baseName}_{index}.pkl");
            if (!File.Exists(candidate))
                return candidate;
            index++;
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        cleaned = Regex.Replace(cleaned, @"\s+", "_").Trim('_');
        return string.IsNullOrWhiteSpace(cleaned) ? "sujeto" : cleaned;
    }

    private void RefreshStatus()
    {
        var latestModel = Directory.Exists(_environment.ModelsDirectory)
            ? Directory.EnumerateFiles(_environment.ModelsDirectory, "*.pkl")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault()
            : null;

        if (latestModel is not null)
        {
            Status.RefreshModel(latestModel.FullName);
            LastTrainedModelFileName = latestModel.Name;
            IsModelTrained = true;
        }
        else
        {
            Status.RefreshModel(_environment.ModelPath);
        }

        OnPropertyChanged(nameof(TargetModelFileName));
        OnPropertyChanged(nameof(ModelFileName));
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
        var targetModelPath = ResolveTargetModelPath();
        var targetFileName = Path.GetFileName(targetModelPath);
        var algorithm = SelectedAlgorithm ?? TrainingAlgorithm.Default;

        IsTraining = true;
        ShowMessage("Preparando datasets…");
        try
        {
            var progress = new Progress<string>(line =>
            {
                if (!string.IsNullOrWhiteSpace(line)) ShowMessage(line.Trim());
            });

            var result = await _trainModel.ExecuteAsync(selections, progress, algorithm.Id, targetModelPath);
            if (result.Succeeded && result.Training is { } training)
            {
                AccuracyText = training.Accuracy.ToString("P1", Es);
                TrainedClassesText = string.Join(" · ", selections.Select(s => s.Description));
                LastTrainedModelFileName = targetFileName;
                IsModelTrained = true;
                ShowMessage($"Modelo {algorithm.DisplayName} guardado en {targetFileName}. " +
                            $"Exactitud {AccuracyText} sobre una división aleatoria de ventanas, no por sesión ni por sujeto.");
                Status.RefreshModel(targetModelPath);
                OnPropertyChanged(nameof(TargetModelFileName));
                OnPropertyChanged(nameof(ModelFileName));
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
        !IsTraining &&
        !Status.IsCapturing &&
        Movements.Count(m => m.IsReady) >= 2 &&
        !string.IsNullOrWhiteSpace(PersonName) &&
        string.IsNullOrEmpty(PersonNameWarning);

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

        // Si el usuario tiene el nombre inicial o vacío y los movimientos listos comparten un sujeto no nulo:
        var selectedSubjects = ready.Select(m => m.SelectedSubject.Name).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
        if (selectedSubjects.Count == 1 && (PersonName == "Sujeto1" || string.IsNullOrWhiteSpace(PersonName)))
        {
            PersonName = selectedSubjects[0]!;
        }

        TrainModelCommand.NotifyCanExecuteChanged();
    }

    private void ShowMessage(string text, bool isError = false)
    {
        Message = text;
        IsError = isError;
    }
}
