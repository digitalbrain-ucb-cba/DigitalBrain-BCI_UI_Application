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
/// Pantalla «Tiempo real» (paso 3): lanza el capturador con el modelo elegido y muestra la clase de las últimas
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
    private readonly IDialogService _dialogs;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private CancellationTokenSource? _predictionCts;
    private Dictionary<string, MovementClass> _movements = [];
    private int _predictionCount;

    public RealtimeViewModel(
        INavigationService navigationService,
        IRealtimeGateway gateway,
        IBackendEnvironment environment,
        ISettingsStore settings,
        IDataCatalog catalog,
        IDialogService dialogs,
        SystemStatusViewModel status)
    {
        Title = "Tiempo real";
        _navigationService = navigationService;
        _gateway = gateway;
        _environment = environment;
        _settings = settings;
        _catalog = catalog;
        _dialogs = dialogs;
        Status = status;
        Status.PropertyChanged += OnStatusChanged;
    }

    public SystemStatusViewModel Status { get; }
    public ObservableCollection<ModelOption> Models { get; } = [];
    /// <summary>Últimas predicciones, la más reciente primero.</summary>
    public ObservableCollection<PredictionItem> RecentPredictions { get; } = [];

    [ObservableProperty] private string _executablePath = "";
    [ObservableProperty] private ModelOption? _selectedModel;
    [ObservableProperty] private bool _isPredicting;
    [ObservableProperty] private string _currentPrediction = NoPrediction;
    [ObservableProperty] private string _currentConfidence = NoPrediction;
    [ObservableProperty] private string _predictionCountText = "0";
    [ObservableProperty] private string _deviceStatus = "";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private bool _isError;

    public string ToggleText => IsPredicting ? "Detener predicción" : "Iniciar predicción";

    public override void OnNavigatedTo()
    {
        // La ruta del capturador se cambia en la pantalla Capturar.
        ExecutablePath = _settings.Load().CaptureExecutablePath;
        if (!IsPredicting) ReloadModels();
    }

    /// <summary>Lista los modelos de la carpeta; conserva el elegido, aunque venga de otra carpeta, o toma el más reciente.</summary>
    private void ReloadModels()
    {
        var selected = SelectedModel;
        Models.Clear();
        foreach (var file in _environment.GetModelFiles())
            Models.Add(new ModelOption(file));
        if (selected is not null && !Models.Contains(selected) && File.Exists(selected.FilePath))
            Models.Add(selected);
        SelectedModel = Models.Contains(selected!) ? selected : Models.FirstOrDefault();
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
        if (_dialogs.PickModel(SelectedModel?.FilePath ?? _environment.ModelsDirectory + Path.DirectorySeparatorChar) is not { } path)
            return;
        var model = new ModelOption(path);
        if (!Models.Contains(model)) Models.Add(model);
        SelectedModel = model;
    }

    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanTogglePrediction))]
    private async Task TogglePredictionAsync()
    {
        if (IsPredicting)
        {
            Stop();
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
