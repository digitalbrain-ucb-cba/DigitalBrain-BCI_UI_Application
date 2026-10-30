using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DigitalBrain.UI.Models;
using DigitalBrain.UI.Services.Navigation;
using DigitalBrain.UI.UseCases.Ports;

namespace DigitalBrain.UI.ViewModels;

/// <summary>
/// Pantalla «Inicio» (Resumen del sistema): presenta el flujo biomédico de extremo a extremo,
/// indicadores clave de adquisición, configuración global de frecuencia de muestreo y accesos directos al flujo de trabajo.
/// </summary>
public partial class HomeViewModel : BaseViewModel
{
    private readonly INavigationService _navigationService;
    private readonly IRecordingRepository _recordings;
    private readonly IDataCatalog _catalog;
    private readonly IBackendEnvironment _environment;
    private readonly ISettingsStore _settings;

    public HomeViewModel(
        INavigationService navigationService,
        SystemStatusViewModel status,
        IRecordingRepository recordings,
        IDataCatalog catalog,
        IBackendEnvironment environment,
        ISettingsStore settings)
    {
        Title = "Inicio";
        _navigationService = navigationService;
        Status = status;
        _recordings = recordings;
        _catalog = catalog;
        _environment = environment;
        _settings = settings;

        // Cargar frecuencia inicial persistida o actual
        var savedRate = settings.Load().SampleRateHz;
        if (savedRate > 0)
        {
            AcquisitionParameters.SampleRateHz = savedRate;
        }

        _sampleRateInput = AcquisitionParameters.SampleRateHz.ToString("0");
        _sampleRateText = $"{AcquisitionParameters.SampleRateHz:0} Hz";
        _sampleRateDetail = $"Una muestra cada {(1000.0 / AcquisitionParameters.SampleRateHz):0.#} ms";

        AcquisitionParameters.SampleRateChanged += () =>
        {
            var rate = AcquisitionParameters.SampleRateHz;
            SampleRateText = $"{rate:0} Hz";
            SampleRateDetail = $"Una muestra cada {(1000.0 / rate):0.#} ms";
            if (SampleRateInput != rate.ToString("0"))
            {
                SampleRateInput = rate.ToString("0");
            }
        };

        _ = LoadAsync();
    }

    public SystemStatusViewModel Status { get; }

    // ---- Indicadores principales (KPIs) ----
    [ObservableProperty] private string _activeChannelsText = $"{AcquisitionParameters.ChannelNames.Count} / {AcquisitionParameters.ChannelNames.Count}";
    [ObservableProperty] private string _activeChannelsDetail = "A, B, C y D · 4 canales";

    [ObservableProperty] private string _sampleRateInput;
    [ObservableProperty] private string _sampleRateText;
    [ObservableProperty] private string _sampleRateDetail;

    [ObservableProperty] private string _featuresText = "16 por ventana";
    [ObservableProperty] private string _featuresDetail = "4 bandas × 4 canales";
    [ObservableProperty] private string _deviceStatusText = "Listo";
    [ObservableProperty] private string _deviceStatusDetail = "ConDumpAuto · Puerto USB";
    [ObservableProperty] private string _modelStatusText = "ModelMotores.pkl";
    [ObservableProperty] private string _modelStatusDetail = "Random Forest · 16 feat";
    [ObservableProperty] private string _modelAccuracyText = "96,8 %";
    [ObservableProperty] private string _modelAccuracyDetail = "Validado por grabación";
    [ObservableProperty] private string _responseTimeText = "0,5 s";
    [ObservableProperty] private string _responseTimeDetail = "Nueva decisión cada 63 muestras";

    // ---- Datos dinámicos del flujo ----
    [ObservableProperty] private string _recordingsSummary = "50 grabaciones · 3 movimientos";
    [ObservableProperty] private int _recordingsCount = 50;
    [ObservableProperty] private bool _hasTrainedModel;

    partial void OnSampleRateInputChanged(string value)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var rate) ||
            double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out rate))
        {
            if (rate is >= 1 and <= 10000)
            {
                AcquisitionParameters.SampleRateHz = rate;
                SampleRateText = $"{rate:0} Hz";
                SampleRateDetail = $"Una muestra cada {(1000.0 / rate):0.#} ms";

                var current = _settings.Load();
                _settings.Save(current with { SampleRateHz = rate });
            }
        }
    }

    public async Task LoadAsync()
    {
        try
        {
            var count = await _recordings.CountAsync();
            RecordingsCount = count;
            Status.RecordingCount = count;

            // Movimientos con al menos una captura, contando las de todos sus sujetos.
            var movementsCount = _catalog.GetMovements().Count(m => _catalog.CountRecordings(m, null) > 0);
            RecordingsSummary = count > 0
                ? $"{count} grabaciones · {movementsCount} movimientos"
                : "Sin grabaciones registradas";

            HasTrainedModel = File.Exists(_environment.ModelPath);
            Status.IsModelReady = HasTrainedModel;
            Status.ModelDetail = HasTrainedModel ? Path.GetFileName(_environment.ModelPath) : "sin entrenar";

            ModelStatusText = HasTrainedModel ? Path.GetFileName(_environment.ModelPath) : "Sin entrenar";
            ModelStatusDetail = HasTrainedModel ? "Random Forest · 16 feat" : "Pendiente de entrenamiento";
        }
        catch
        {
            // Fallback seguro a valores por defecto en caso de acceso a disco bloqueado
        }
    }

    [RelayCommand]
    private void SetPresetSampleRate(string hz)
    {
        SampleRateInput = hz;
    }

    [RelayCommand]
    private void NavigateCapture() => _navigationService.NavigateTo<CaptureViewModel>();

    [RelayCommand]
    private void NavigateTraining() => _navigationService.NavigateTo<TrainingViewModel>();

    [RelayCommand]
    private void NavigateDatabase() => _navigationService.NavigateTo<DatabaseViewModel>();

    [RelayCommand]
    private void NavigateRealtime() => _navigationService.NavigateTo<RealtimeViewModel>();
}
