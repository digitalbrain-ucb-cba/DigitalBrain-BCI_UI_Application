using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DigitalBrain.UI.Models;
using DigitalBrain.UI.Services.Dialogs;
using DigitalBrain.UI.Services.Navigation;
using DigitalBrain.UI.UseCases.Ports;

namespace DigitalBrain.UI.ViewModels;

/// <summary>Opción de la lista de modelos: se ve el nombre del archivo y se usa su ruta.</summary>
public sealed record ModelOption(string FilePath)
{
    public string Name => Path.GetFileName(FilePath);
    // ComboStyle dibuja el elemento elegido con ToString(), no con DisplayMemberPath.
    public override string ToString() => Name;
}

/// <summary>Fila del historial de predicciones.</summary>
public sealed record PredictionItem(string NumberText, string Name, string AccentHex, string ConfidenceText);

/// <summary>
/// Pantalla «Tiempo real» (paso 3): elige sujeto, tipo (MI o M) y uno de sus modelos, lanza el capturador con él y muestra la clase de las últimas
/// ventanas con la confianza del modelo en cada una. En vivo no se conoce el movimiento real, así que esa confianza
/// es lo único que se puede mostrar: no es una exactitud medida.
/// El envío de comandos al robot UGOT todavía no está conectado.
/// </summary>
public partial class RealtimeViewModel : BaseViewModel
{
    private const string NoPrediction = "—";
    private const string UnknownAccentHex = "#8A8A8A";
    private const int HistorySize = 20;
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    private readonly INavigationService _navigationService;
    private readonly IRealtimeGateway _gateway;
    private readonly IBackendEnvironment _environment;
    private readonly ISettingsStore _settings;
    private readonly IDataCatalog _catalog;
    private readonly IModelStore _models;
    private readonly IDialogService _dialogs;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private CancellationTokenSource? _predictionCts;
    private Dictionary<string, MovementClass> _movements = [];
    private int _predictionCount;
    /// <summary>Modelos de la lista actual, para mostrar los datos de su entrenamiento.</summary>
    private IReadOnlyList<ModelInfo> _listedModels = [];
    /// <summary>Al recargar los sujetos se fija el tipo a mano: entonces el cambio de sujeto no debe elegir otro.</summary>
    private bool _isReloading;

    public RealtimeViewModel(
        INavigationService navigationService,
        IRealtimeGateway gateway,
        IBackendEnvironment environment,
        ISettingsStore settings,
        IDataCatalog catalog,
        IModelStore models,
        IDialogService dialogs,
        SystemStatusViewModel status)
    {
        Title = "Tiempo real";
        _navigationService = navigationService;
        _gateway = gateway;
        _environment = environment;
        _settings = settings;
        _catalog = catalog;
        _models = models;
        _dialogs = dialogs;
        Status = status;
        Status.PropertyChanged += OnStatusChanged;
    }

    public SystemStatusViewModel Status { get; }
    public ObservableCollection<string> Subjects { get; } = [];
    public IReadOnlyList<ModelType> ModelTypes => ModelType.All;
    public ObservableCollection<ModelOption> Models { get; } = [];
    /// <summary>Últimas predicciones, la más reciente primero.</summary>
    public ObservableCollection<PredictionItem> RecentPredictions { get; } = [];

    [ObservableProperty] private string _executablePath = "";
    [ObservableProperty] private string? _selectedSubject;
    [ObservableProperty] private ModelType _selectedModelType = ModelType.All[0];
    [ObservableProperty] private ModelOption? _selectedModel;
    [ObservableProperty] private string _modelHint = "";
    [ObservableProperty] private string _selectedModelDetail = "";
    [ObservableProperty] private bool _isPredicting;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ToggleText))] private bool _isStopping;
    [ObservableProperty] private string _currentPrediction = NoPrediction;
    [ObservableProperty] private string _currentConfidence = NoPrediction;
    [ObservableProperty] private string _predictionCountText = "0";
    [ObservableProperty] private string _deviceStatus = "";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private bool _isError;

    public string ToggleText => IsStopping ? "Deteniendo…" : IsPredicting ? "Detener predicción" : "Iniciar predicción";

    public override void OnNavigatedTo()
    {
        // La ruta del capturador se cambia en la pantalla Capturar.
        ExecutablePath = _settings.Load().CaptureExecutablePath;
        if (!IsPredicting) ReloadSubjects();
    }

    /// <summary>
    /// Lista los sujetos que tienen algún modelo. Conserva sujeto, tipo y modelo elegidos; la primera vez toma los del modelo
    /// más reciente.
    /// </summary>
    private void ReloadSubjects()
    {
        IReadOnlyList<ModelInfo> all;
        try
        {
            all = _models.GetAllModels();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowMessage($"No se pudieron leer los modelos de {_environment.DataRoot}: {ex.Message}", isError: true);
            all = [];
        }

        var keepModel = SelectedModel;
        var latest = all.FirstOrDefault(m => m.Subject is not null && m.Type is not null);
        var keepSubject = SelectedSubject ?? latest?.Subject;
        var keepType = SelectedSubject is null && latest?.Type is { } latestType ? latestType : SelectedModelType;

        _isReloading = true;
        try
        {
            Subjects.Clear();
            foreach (var subject in all.Where(m => m.Type is not null).Select(m => m.Subject).OfType<string>()
                         .Distinct(StringComparer.CurrentCultureIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase))
                Subjects.Add(subject);

            SelectedModelType = keepType;
            // Forzar la recarga aunque el sujeto no cambie: sus modelos pudieron cambiar fuera.
            SelectedSubject = null;
            SelectedSubject = Subjects.FirstOrDefault(s => string.Equals(s, keepSubject, StringComparison.CurrentCultureIgnoreCase))
                              ?? Subjects.FirstOrDefault();
        }
        finally
        {
            _isReloading = false;
        }
        PickTypeWithModels();
        ReloadModels(keepModel ?? (latest is null ? null : new ModelOption(latest.FilePath)));
    }

    partial void OnSelectedSubjectChanged(string? value)
    {
        if (_isReloading) return;
        PickTypeWithModels();
        ReloadModels(keep: null);
    }

    partial void OnSelectedModelTypeChanged(ModelType value)
    {
        if (!_isReloading) ReloadModels(keep: null);
    }

    /// <summary>Si el sujeto no tiene modelos del tipo elegido pero sí del otro, elige ese.</summary>
    private void PickTypeWithModels()
    {
        if (SelectedSubject is not { } subject || _models.GetModels(subject, SelectedModelType).Count > 0) return;
        if (ModelType.All.FirstOrDefault(type => _models.GetModels(subject, type).Count > 0) is { } available)
        {
            _isReloading = true;
            SelectedModelType = available;
            _isReloading = false;
        }
    }

    /// <summary>Lista los modelos del sujeto y tipo elegidos; conserva <paramref name="keep"/> si está o toma el más reciente.</summary>
    private void ReloadModels(ModelOption? keep)
    {
        _listedModels = SelectedSubject is { } subject ? _models.GetModels(subject, SelectedModelType) : [];
        Models.Clear();
        foreach (var model in _listedModels)
            Models.Add(new ModelOption(model.FilePath));
        SelectedModel = Models.FirstOrDefault(m => m == keep) ?? Models.FirstOrDefault();

        ModelHint = SelectedSubject is null
            ? "Aún no hay modelos: entrena uno en Entrenar."
            : Models.Count == 0
                ? $"{SelectedSubject} no tiene modelos {SelectedModelType.Folder}: entrénalo en Entrenar."
                : "";
    }

    partial void OnSelectedModelChanged(ModelOption? value)
    {
        var info = _listedModels.FirstOrDefault(m => value is not null && m.FilePath == value.FilePath);
        SelectedModelDetail = info is null
            ? value is null ? "" : "Modelo elegido a mano"
            : string.Join(" · ", new[]
            {
                info.Algorithm?.DisplayName,
                info.Accuracy is { } accuracy ? $"exactitud {accuracy.ToString("P1", Es)}" : null,
                info.TrainedAt.ToString("dd/MM/yy HH:mm", Es),
            }.OfType<string>());
        if (value is not null)
            Status.RefreshModel(value.FilePath);
    }

    partial void OnIsPredictingChanged(bool value)
    {
        Status.IsPredicting = value;
        OnPropertyChanged(nameof(ToggleText));
    }

    private void OnStatusChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SystemStatusViewModel.IsCapturing) or nameof(SystemStatusViewModel.IsTraining))
            TogglePredictionCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void BrowseModel()
    {
        if (_dialogs.PickModel(SelectedModel?.FilePath ?? _environment.DataRoot + Path.DirectorySeparatorChar) is not { } path)
            return;
        var model = new ModelOption(path);
        // Si es de la carpeta de un sujeto, se elige ese sujeto y tipo; si no, se añade a la lista actual.
        var known = _models.GetAllModels().FirstOrDefault(m => m.Type is not null &&
            string.Equals(Path.GetFullPath(m.FilePath), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));
        if (known is { Subject: { } subject, Type: { } type })
        {
            _isReloading = true;
            SelectedModelType = type;
            SelectedSubject = Subjects.FirstOrDefault(s => string.Equals(s, subject, StringComparison.CurrentCultureIgnoreCase));
            _isReloading = false;
            ReloadModels(new ModelOption(known.FilePath));
            return;
        }
        if (!Models.Contains(model)) Models.Add(model);
        SelectedModel = model;
        ModelHint = "";
    }

    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanTogglePrediction))]
    private async Task TogglePredictionAsync()
    {
        if (IsPredicting)
        {
            if (!IsStopping)
            {
                IsStopping = true;
                ShowMessage("Cerrando el capturador…");
                Stop();
            }
            return;
        }

        ExecutablePath = _settings.Load().CaptureExecutablePath;
        if (!File.Exists(ExecutablePath))
        {
            ShowMessage("No se encontró el capturador en este equipo. Su ruta se configura en la pantalla Capturar.", isError: true);
            return;
        }
        if (SelectedModel is not { } model || !File.Exists(model.FilePath))
        {
            ShowMessage("No se encontró el modelo: entrénalo en Entrenar o elige un archivo .pkl.", isError: true);
            return;
        }

        _movements = _catalog.GetMovements().ToDictionary(m => m.Label);
        _predictionCount = 0;
        PredictionCountText = "0";
        CurrentPrediction = NoPrediction;
        CurrentConfidence = NoPrediction;
        RecentPredictions.Clear();
        DeviceStatus = "";
        ShowMessage("Iniciando; la primera decisión llega tras 1 s de señal válida.");
        _predictionCts = new CancellationTokenSource();
        IsPredicting = true;

        try
        {
            var outcome = await _gateway.RunAsync(
                new RealtimeRequest(ExecutablePath, model.FilePath), OnPrediction, OnDeviceLog, _predictionCts.Token);
            if (outcome.Cancelled)
                ShowMessage("Predicción detenida.");
            else
                ShowMessage(outcome.Error ?? "La predicción terminó.", isError: true);
        }
        catch (Exception ex)
        {
            ShowMessage($"Error durante la predicción: {ex.Message}", isError: true);
        }
        finally
        {
            IsPredicting = false;
            IsStopping = false;
            Status.IsHeadsetStreaming = false;
            _predictionCts.Dispose();
            _predictionCts = null;
        }
    }

    private bool CanTogglePrediction() => !Status.IsCapturing && !Status.IsTraining;

    /// <summary>Detiene la predicción en curso, si hay una; también se llama al cerrar la aplicación.</summary>
    public void Stop() => _predictionCts?.Cancel();

    private void OnPrediction(RealtimePrediction prediction) => _dispatcher.InvokeAsync(() =>
    {
        Status.IsHeadsetStreaming = IsPredicting;
        _predictionCount++;
        PredictionCountText = _predictionCount.ToString("N0", Es);

        var movement = _movements.GetValueOrDefault(prediction.Label);
        CurrentPrediction = movement?.DisplayName ?? prediction.Label;
        CurrentConfidence = prediction.Confidence?.ToString("P0", Es) ?? NoPrediction;

        RecentPredictions.Insert(0, new PredictionItem(
            $"#{PredictionCountText}", CurrentPrediction, movement?.AccentHex ?? UnknownAccentHex, CurrentConfidence));
        if (RecentPredictions.Count > HistorySize)
            RecentPredictions.RemoveAt(HistorySize);
    });

    private void OnDeviceLog(string line) => _dispatcher.InvokeAsync(() => DeviceStatus = line);

    private void ShowMessage(string text, bool isError = false)
    {
        Message = text;
        IsError = isError;
    }

    [RelayCommand]
    private void NavigateCapture() => _navigationService.NavigateTo<CaptureViewModel>();
}
