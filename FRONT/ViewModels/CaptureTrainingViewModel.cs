using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DigitalBrain.UI.Models;
using DigitalBrain.UI.Services.Dialogs;
using DigitalBrain.UI.Services.Signal;
using DigitalBrain.UI.UseCases;
using DigitalBrain.UI.UseCases.Ports;

namespace DigitalBrain.UI.ViewModels;

/// <summary>
/// Pantalla «Captura y entrenamiento» (pasos 1 y 2): configura la sesión, graba con el backend,
/// muestra los cuatro canales en vivo y entrena el modelo con las capturas guardadas.
/// </summary>
public partial class CaptureTrainingViewModel : BaseViewModel
{
    private int VisibleSamples => (int)Math.Round(AcquisitionParameters.SampleRateHz * 4.0); // 4 s a la frecuencia configurada
    private const int RecentCount = 4;
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    private readonly RecordSessionUseCase _recordSession;
    private readonly TrainModelUseCase _trainModel;
    private readonly IRecordingRepository _recordings;
    private readonly IBackendEnvironment _environment;
    private readonly ISettingsStore _settings;
    private readonly IDialogService _dialogs;
    private readonly SignalRingBuffer _buffer;
    private readonly CaptureStatistics _statistics = new();
    private readonly DispatcherTimer _refreshTimer;
    private readonly Stopwatch _clock = new();
    private CancellationTokenSource? _captureCts;
    private double _captureFromSeconds = AcquisitionParameters.DefaultCaptureFromSeconds;
    private double _captureTotalSeconds;

    public CaptureTrainingViewModel(
        RecordSessionUseCase recordSession,
        TrainModelUseCase trainModel,
        IRecordingRepository recordings,
        IBackendEnvironment environment,
        ISettingsStore settings,
        IDialogService dialogs,
        SignalRingBuffer buffer,
        SystemStatusViewModel status)
    {
        Title = "Captura y entrenamiento";
        _recordSession = recordSession;
        _trainModel = trainModel;
        _recordings = recordings;
        _environment = environment;
        _settings = settings;
        _dialogs = dialogs;
        _buffer = buffer;
        Status = status;

        Channels =
        [
            new ChannelViewModel("A", "Channel1Brush"),
            new ChannelViewModel("B", "Channel2Brush"),
            new ChannelViewModel("C", "Channel3Brush"),
            new ChannelViewModel("D", "Channel4Brush"),
        ];

        _executablePath = settings.Load().ExecutablePath ?? "";
        _refreshTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) => RefreshLive(), Dispatcher.CurrentDispatcher);

        SelectedMovement = Movements[0];
        UpdateRecordingCounters(CaptureSnapshot.Empty, TimeSpan.Zero);
        AcquisitionParameters.SampleRateChanged += () =>
        {
            OnPropertyChanged(nameof(FrequencyText));
            OnPropertyChanged(nameof(WindowText));
            OnPropertyChanged(nameof(StepText));
            OnPropertyChanged(nameof(OverlapText));
            _ = LoadAsync();
        };
        _ = LoadAsync();
    }

    public SystemStatusViewModel Status { get; }
    public IReadOnlyList<MovementClass> Movements { get; } = MovementCatalog.All;
    public IReadOnlyList<ChannelViewModel> Channels { get; }
    public ObservableCollection<RecordingItemViewModel> RecentRecordings { get; } = [];

    // ---- Configuración de sesión ----
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(FileNamePreview))] private string _subjectId = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(FileNamePreview))] private MovementClass? _selectedMovement;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(FileNamePreview))] private string _sessionNumberText = "1";
    [ObservableProperty] private string _durationText = "25";
    [ObservableProperty] private string _notes = "";
    [ObservableProperty] private string _executablePath;
    [ObservableProperty] private string _outputFolder = "";

    public string FileNamePreview =>
        SelectedMovement is not null && int.TryParse(SessionNumberText, out var number) && number > 0
            ? SelectedMovement.FileNameFor(number)
            : "—";

    // ---- Parámetros avanzados (solo lectura, valores del backend) ----
    public string FrequencyText => $"{AcquisitionParameters.SampleRateHz:0} Hz";
    public string WindowText => $"{AcquisitionParameters.WindowSize} muestras · {(AcquisitionParameters.WindowSize / AcquisitionParameters.SampleRateHz).ToString("0.#", Es)} s";
    public string StepText => $"{AcquisitionParameters.WindowStep} muestras · {(AcquisitionParameters.WindowStep / AcquisitionParameters.SampleRateHz).ToString("0.0", Es)} s";
    public string OverlapText => $"{(AcquisitionParameters.OverlapRatio * 100).ToString("0", Es)} %";
    public string SaturationText { get; } = $"±{AcquisitionParameters.SaturationMicroVolts:0} µV";
    public string ChannelsText { get; } = string.Join(", ", AcquisitionParameters.ChannelNames);

    // ---- Grabación ----
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(TrainModelCommand))] private bool _isCapturing;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(ToggleCaptureCommand))] private bool _isTraining;
    [ObservableProperty] private string _elapsedText = "00:00";
    [ObservableProperty] private string _durationLabel = "de 00:25";
    [ObservableProperty] private string _validSamplesText = "0";
    [ObservableProperty] private string _discardedText = "0";
    [ObservableProperty] private string _recordingState = "Lista para grabar";
    [ObservableProperty] private string _recordingFile = "";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private bool _isError;
    [ObservableProperty] private string _viewAllText = "Ver todas →";

    partial void OnSelectedMovementChanged(MovementClass? value)
    {
        if (value is null) return;
        OutputFolder = value.DataFolder + Path.DirectorySeparatorChar;
    }

    partial void OnOutputFolderChanged(string value) => SuggestSessionNumber();

    partial void OnDurationTextChanged(string value)
    {
        if (!IsCapturing)
            DurationLabel = $"de {FormatTime(TimeSpan.FromSeconds(ParseDuration() ?? 0))}";
    }

    partial void OnExecutablePathChanged(string value) =>
        _settings.Save(_settings.Load() with { ExecutablePath = value });

    partial void OnSessionNumberTextChanged(string value)
    {
        if (!IsCapturing) RecordingFile = FileNamePreview;
    }

    [RelayCommand]
    private void BrowseExecutable()
    {
        if (_dialogs.PickExecutable(ExecutablePath) is { } path)
            ExecutablePath = path;
    }

    [RelayCommand]
    private void BrowseOutputFolder()
    {
        if (_dialogs.PickFolder(_environment.Resolve(OutputFolder)) is { } folder)
            OutputFolder = ToDisplayPath(folder);
    }

    [RelayCommand]
    private void OpenRecordings() => _dialogs.OpenFolder(_environment.DataRoot);

    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanToggleCapture))]
    private async Task ToggleCaptureAsync()
    {
        if (IsCapturing)
        {
            _captureCts?.Cancel();
            return;
        }

        var plan = BuildPlan();
        if (plan is null) return;

        var errors = plan.Validate();
        if (errors.Count > 0)
        {
            ShowMessage(string.Join(" ", errors), isError: true);
            return;
        }

        _captureFromSeconds = plan.CaptureFromSeconds;
        _captureTotalSeconds = plan.DurationSeconds;
        _buffer.Clear();
        _statistics.Reset();
        _captureCts = new CancellationTokenSource();
        IsCapturing = true;
        Status.IsHeadsetStreaming = false;
        RecordingState = "Grabando";
        RecordingFile = plan.FileName;
        DurationLabel = $"de {FormatTime(TimeSpan.FromSeconds(plan.DurationSeconds))}";
        ShowMessage($"Iniciando {Path.GetFileName(plan.ExecutablePath)} con la CLI de captura; se guarda desde el segundo {plan.CaptureFromSeconds:0}.");
        _clock.Restart();
        _refreshTimer.Start();

        try
        {
            var result = await _recordSession.ExecuteAsync(plan, OnSample, OnBackendLog, _captureCts.Token);
            _refreshTimer.Stop();
            _clock.Stop();
            RefreshLive();

            if (result.Succeeded && result.Recording is { } recording)
            {
                ValidSamplesText = recording.ValidSamples.ToString("N0", Es);
                DiscardedText = recording.DiscardedSamples.ToString("N0", Es);
                ShowMessage($"{recording.FileName}: {recording.ValidSamples.ToString("N0", Es)} muestras válidas y {recording.Windows} ventanas para el dataset.");
                await LoadAsync();
            }
            else if (result.Cancelled)
            {
                ShowMessage("Se detuvo la captura y se descartó el archivo parcial.");
            }
            else
            {
                ShowMessage(string.Join(" ", result.Errors), isError: true);
            }
        }
        catch (Exception ex)
        {
            ShowMessage($"Error durante la captura: {ex.Message}", isError: true);
        }
        finally
        {
            _refreshTimer.Stop();
            IsCapturing = false;
            Status.IsHeadsetStreaming = false;
            _captureCts.Dispose();
            _captureCts = null;
            RecordingState = "Lista para grabar";
            SuggestSessionNumber();
        }
    }

    private bool CanToggleCapture() => !IsTraining;

    [RelayCommand(CanExecute = nameof(CanTrain))]
    private async Task TrainModelAsync()
    {
        IsTraining = true;
        ShowMessage("Preparando datasets…");
        try
        {
            var progress = new Progress<string>(line =>
            {
                if (!string.IsNullOrWhiteSpace(line)) ShowMessage(line.Trim());
            });
            var result = await _trainModel.ExecuteAsync(progress);
            if (result.Succeeded && result.Training is { } training)
            {
                var classes = string.Join(", ", result.Classes.Select(c => c.DisplayName.ToLowerInvariant()));
                ShowMessage($"Modelo guardado en {Path.GetFileName(training.ModelPath)} ({classes}). " +
                            $"Exactitud {training.Accuracy.ToString("P1", Es)} sobre una división aleatoria de ventanas, no por sesión.");
                RefreshModelStatus();
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

    private bool CanTrain() => !IsCapturing;

    private void OnSample(SignalSample sample)
    {
        _buffer.Add(sample);
        // Solo cuenta lo que la CLI guarda: entre --capture-from y --seconds.
        if (sample.TimeSeconds >= _captureFromSeconds && sample.TimeSeconds < _captureTotalSeconds)
            _statistics.Register(sample);
    }

    private void OnBackendLog(string line)
    {
        // El cronómetro arranca cuando la CLI lanza el capturador, igual que su plazo.
        if (line.StartsWith("Ejecutando:", StringComparison.Ordinal))
            _clock.Restart();
    }

    private void RefreshLive()
    {
        var samples = _buffer.ToArray();
        if (samples.Length > 0) Status.IsHeadsetStreaming = IsCapturing;

        var visible = samples.Length > VisibleSamples ? samples[^VisibleSamples..] : samples;
        Func<SignalSample, double>[] selectors = [s => s.Channel1, s => s.Channel2, s => s.Channel3, s => s.Channel4];
        for (int i = 0; i < Channels.Count; i++)
        {
            var values = visible.Select(selectors[i]).ToArray();
            var peak = values.Length == 0 ? 0 : values.Max(Math.Abs);
            Channels[i].Samples = values;
            Channels[i].Range = Math.Max(peak * 1.1, 20);
            Channels[i].RangeText = $"±{peak:0} µV";
        }

        var elapsed = _clock.Elapsed;
        if (ParseDuration() is { } total && elapsed.TotalSeconds > total)
            elapsed = TimeSpan.FromSeconds(total);
        UpdateRecordingCounters(_statistics.Snapshot(), elapsed);
    }

    private void UpdateRecordingCounters(CaptureSnapshot snapshot, TimeSpan elapsed)
    {
        ElapsedText = FormatTime(elapsed);
        ValidSamplesText = snapshot.Valid.ToString("N0", Es);
        DiscardedText = snapshot.Discarded.ToString("N0", Es);
        for (int i = 0; i < Channels.Count; i++)
        {
            var ratio = snapshot.ChannelValidRatios[i];
            Channels[i].ValidRatio = ratio ?? 0;
            Channels[i].ValidText = ratio is null ? "—" : ratio.Value.ToString("P1", Es);
        }
    }

    private async Task LoadAsync()
    {
        try
        {
            var count = await _recordings.CountAsync();
            var latest = await _recordings.GetLatestAsync(RecentCount);
            Status.RecordingCount = count;
            ViewAllText = $"Ver las {count} →";
            RecentRecordings.Clear();
            foreach (var recording in latest)
                RecentRecordings.Add(new RecordingItemViewModel(recording));
        }
        catch (Exception ex)
        {
            ShowMessage($"No se pudieron leer las grabaciones de {_environment.DataRoot}: {ex.Message}", isError: true);
        }
        RefreshModelStatus();
    }

    private void RefreshModelStatus()
    {
        Status.IsModelReady = File.Exists(_environment.ModelPath);
        Status.ModelDetail = Status.IsModelReady ? Path.GetFileName(_environment.ModelPath) : "sin entrenar";
    }

    private void SuggestSessionNumber()
    {
        if (SelectedMovement is null || IsCapturing) return;
        SessionNumberText = _recordings.NextSessionNumber(SelectedMovement, _environment.Resolve(OutputFolder)).ToString(CultureInfo.InvariantCulture);
        RecordingFile = FileNamePreview;
    }

    private RecordingPlan? BuildPlan()
    {
        if (SelectedMovement is null)
        {
            ShowMessage("Elige el movimiento o clase.", isError: true);
            return null;
        }
        if (!int.TryParse(SessionNumberText, out var session))
        {
            ShowMessage("El número de sesión debe ser un entero.", isError: true);
            return null;
        }
        if (ParseDuration() is not { } duration)
        {
            ShowMessage("La duración debe ser un número de segundos.", isError: true);
            return null;
        }

        return new RecordingPlan(
            SubjectId.Trim(), SelectedMovement, session, duration,
            string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
            ExecutablePath.Trim().Trim('"'),
            _environment.Resolve(OutputFolder));
    }

    private double? ParseDuration() =>
        double.TryParse(DurationText.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0
            ? seconds
            : null;

    private string ToDisplayPath(string folder)
    {
        var relative = Path.GetRelativePath(_environment.BackendRoot, folder);
        var display = relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? folder : relative;
        return Path.TrimEndingDirectorySeparator(display) + Path.DirectorySeparatorChar;
    }

    private void ShowMessage(string text, bool isError = false)
    {
        Message = text;
        IsError = isError;
    }

    private static string FormatTime(TimeSpan time) => $"{(int)time.TotalMinutes:00}:{time.Seconds:00}";
}
