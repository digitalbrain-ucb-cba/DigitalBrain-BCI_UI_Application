using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DigitalBrain.UI.Models;
using DigitalBrain.UI.Services.Navigation;
using DigitalBrain.UI.UseCases.Ports;

namespace DigitalBrain.UI.ViewModels;

/// <summary>Estado de un paso del flujo en la pantalla de inicio.</summary>
/// <param name="IsNext">Es el paso que conviene hacer ahora: se resalta.</param>
public sealed record StepStatus(string Text, bool IsDone, bool IsNext)
{
    public static StepStatus Done { get; } = new("Hecho", true, false);
    public static StepStatus Next { get; } = new("Siguiente paso", false, true);
    public static StepStatus Pending { get; } = new("Pendiente", false, false);
    public static StepStatus Running { get; } = new("En curso", true, false);
}

/// <summary>
/// Pantalla «Inicio»: resumen del sistema calculado con lo que hay en disco (grabaciones, sujetos y modelos), el estado del
/// capturador, la frecuencia de muestreo global y en qué paso del flujo está el trabajo.
/// </summary>
public partial class HomeViewModel : BaseViewModel
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    private readonly INavigationService _navigationService;
    private readonly IRecordingRepository _recordings;
    private readonly IDataCatalog _catalog;
    private readonly IModelStore _models;
    private readonly ISettingsStore _settings;
    private bool _hasRecordings;
    private bool _hasCaptureExecutable;

    public HomeViewModel(
        INavigationService navigationService,
        SystemStatusViewModel status,
        IRecordingRepository recordings,
        IDataCatalog catalog,
        IModelStore models,
        ISettingsStore settings)
    {
        Title = "Inicio";
        _navigationService = navigationService;
        Status = status;
        _recordings = recordings;
        _catalog = catalog;
        _models = models;
        _settings = settings;

        var savedRate = settings.Load().SampleRateHz;
        if (savedRate > 0)
            AcquisitionParameters.SampleRateHz = savedRate;

        _sampleRateInput = AcquisitionParameters.SampleRateHz.ToString("0", CultureInfo.InvariantCulture);
        UpdateSampleRateTexts();
        AcquisitionParameters.SampleRateChanged += () =>
        {
            UpdateSampleRateTexts();
            var text = AcquisitionParameters.SampleRateHz.ToString("0", CultureInfo.InvariantCulture);
            if (SampleRateInput != text) SampleRateInput = text;
        };
        Status.PropertyChanged += OnStatusChanged;
        UpdateDevice();
    }

    public SystemStatusViewModel Status { get; }

    // ---- Indicadores ----
    public string ChannelsText { get; } = AcquisitionParameters.ChannelNames.Count.ToString(CultureInfo.InvariantCulture);
    public string ChannelsDetail { get; } = $"{string.Join(", ", AcquisitionParameters.ChannelNames.SkipLast(1))} y {AcquisitionParameters.ChannelNames[^1]}";

    [ObservableProperty] private string _sampleRateInput;
    [ObservableProperty] private string _sampleRateText = "";
    [ObservableProperty] private string _sampleRateDetail = "";

    public string FeaturesText { get; } = $"{AcquisitionParameters.ChannelNames.Count * 4} por ventana";
    public string FeaturesDetail { get; } = $"4 bandas × {AcquisitionParameters.ChannelNames.Count} canales";

    [ObservableProperty] private string _deviceStatusText = "";
    [ObservableProperty] private string _deviceStatusDetail = "";
    [ObservableProperty] private bool _isDeviceReady;

    [ObservableProperty] private string _modelStatusText = "Sin entrenar";
    [ObservableProperty] private string _modelStatusDetail = "Pendiente de entrenamiento";

    // ---- Pasos del flujo ----
    [ObservableProperty] private string _captureSummary = "";
    [ObservableProperty] private StepStatus _captureStep = StepStatus.Next;
    [ObservableProperty] private string _trainingSummary = "";
    [ObservableProperty] private StepStatus _trainingStep = StepStatus.Pending;
    [ObservableProperty] private string _trainingButtonText = "Entrenar modelo";
    [ObservableProperty] private string _realtimeSummary = "";
    [ObservableProperty] private StepStatus _realtimeStep = StepStatus.Pending;
    [ObservableProperty] private bool _hasTrainedModel;

    public override void OnNavigatedTo() => _ = LoadAsync();

    partial void OnSampleRateInputChanged(string value)
    {
        if ((double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var rate) ||
             double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out rate)) && rate is >= 1 and <= 10000)
        {
            AcquisitionParameters.SampleRateHz = rate;
            _settings.Save(_settings.Load() with { SampleRateHz = rate });
        }
    }

    private void UpdateSampleRateTexts()
    {
        var rate = AcquisitionParameters.SampleRateHz;
        SampleRateText = $"{rate:0} Hz";
        SampleRateDetail = $"Una muestra cada {(1000.0 / rate).ToString("0.#", Es)} ms";
    }

    private void OnStatusChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SystemStatusViewModel.IsCapturing) or nameof(SystemStatusViewModel.IsPredicting) or nameof(SystemStatusViewModel.IsTraining)
            or nameof(SystemStatusViewModel.IsHeadsetConnected) or nameof(SystemStatusViewModel.ConnectedPortName))
        {
            UpdateDevice();
            UpdateSteps();
        }
    }

    public async Task LoadAsync()
    {
        try
        {
            var count = await _recordings.CountAsync();
            Status.RecordingCount = count;
            var subjects = _catalog.GetSubjects();
            var movements = _catalog.GetMovements().Count(m => subjects.Any(s => _catalog.CountRecordings(s, m) > 0));
            _hasRecordings = count > 0;
            CaptureSummary = count > 0
                ? $"{Plural(count, "grabación", "grabaciones")} · {Plural(subjects.Count, "sujeto", "sujetos")} · {Plural(movements, "movimiento", "movimientos")}"
                : "Sin grabaciones todavía";

            var latest = _models.GetAllModels().FirstOrDefault();
            HasTrainedModel = latest is not null;
            Status.RefreshModel(latest?.FilePath);
            ModelStatusText = latest?.Name ?? "Sin entrenar";
            ModelStatusDetail = latest is null
                ? "Pendiente de entrenamiento"
                : string.Join(" · ", new[] { latest.Algorithm?.DisplayName, latest.Subject ?? "sin sujeto" }.OfType<string>());
            TrainingSummary = latest switch
            {
                null => "Aún no hay modelos",
                { Accuracy: { } accuracy } => $"Último: exactitud {accuracy.ToString("P1", Es)}",
                _ => $"Último: {latest.Name}",
            };
            TrainingButtonText = HasTrainedModel ? "Volver a entrenar" : "Entrenar modelo";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CaptureSummary = "No se pudo leer la carpeta de datos";
        }
        UpdateDevice();
        UpdateSteps();
    }

    private void UpdateDevice()
    {
        var executable = _settings.Load().CaptureExecutablePath;
        _hasCaptureExecutable = File.Exists(executable);
        var port = string.IsNullOrWhiteSpace(Status.ConnectedPortName) ? "USB" : $"Puerto {Status.ConnectedPortName} · USB";
        (DeviceStatusText, DeviceStatusDetail, IsDeviceReady) = (Status.IsCapturing, Status.IsPredicting, _hasCaptureExecutable, Status.IsHeadsetConnected) switch
        {
            (true, _, _, _) => ("Grabando", "Captura en curso", true),
            (_, true, _, _) => ("Prediciendo", "Clasificación en vivo", true),
            (_, _, false, _) => ("Sin capturador", "Revisa la ruta en Capturar", false),
            (_, _, true, true) => ("Conectado", port, true),
            _ => ("Desconectado", "Conecta el casco por USB", false),
        };
    }

    private void UpdateSteps()
    {
        CaptureStep = Status.IsCapturing ? StepStatus.Running : _hasRecordings ? StepStatus.Done : StepStatus.Next;
        TrainingStep = Status.IsTraining ? StepStatus.Running
            : HasTrainedModel ? StepStatus.Done
            : _hasRecordings ? StepStatus.Next
            : StepStatus.Pending;
        RealtimeStep = Status.IsPredicting ? StepStatus.Running
            : HasTrainedModel && IsDeviceReady ? StepStatus.Next
            : StepStatus.Pending;
        var missingDevice = _hasCaptureExecutable ? "conectar el casco" : "el capturador";
        RealtimeSummary = (HasTrainedModel, IsDeviceReady) switch
        {
            (true, true) => "Modelo y casco listos",
            (false, true) => "Falta entrenar un modelo",
            (true, false) => $"Falta {missingDevice}",
            _ => $"Faltan el modelo y {missingDevice}",
        };
    }

    private static string Plural(int count, string one, string many) =>
        $"{count.ToString("N0", Es)} {(count == 1 ? one : many)}";

    [RelayCommand]
    private void SetPresetSampleRate(string hz) => SampleRateInput = hz;

    [RelayCommand]
    private void NavigateCapture() => _navigationService.NavigateTo<CaptureViewModel>();

    [RelayCommand]
    private void NavigateTraining() => _navigationService.NavigateTo<TrainingViewModel>();

    [RelayCommand]
    private void NavigateDatabase() => _navigationService.NavigateTo<DatabaseViewModel>();

    [RelayCommand]
    private void NavigateRealtime() => _navigationService.NavigateTo<RealtimeViewModel>();
}
