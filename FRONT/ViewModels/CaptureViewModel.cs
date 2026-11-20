using System.Collections.ObjectModel;
using System.ComponentModel;
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
/// Pantalla «Capturar» (paso 1): elige o añade sujeto, tipo (MI o M) y movimiento, graba con el backend en
/// <c>datos/&lt;sujeto&gt;/&lt;MI|M&gt;/movimientos/&lt;movimiento&gt;/</c> y muestra los cuatro canales en vivo.
/// </summary>
public partial class CaptureViewModel : BaseViewModel
{
    private int VisibleSamples => (int)Math.Round(AcquisitionParameters.SampleRateHz * 4.0); // 4 s a la frecuencia configurada
    private const int RecentCount = 4;
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    private readonly RecordSessionUseCase _recordSession;
    private readonly IRecordingRepository _recordings;
    private readonly IDataCatalog _catalog;
    private readonly IModelStore _models;
    private readonly IBackendEnvironment _environment;
    private readonly ISettingsStore _settings;
    private readonly IDialogService _dialogs;
    private readonly SignalRingBuffer _buffer;
    private readonly CaptureStatistics _statistics = new();
    private readonly DispatcherTimer _refreshTimer;
    private readonly Stopwatch _clock = new();
    private CancellationTokenSource? _captureCts;
    private double _captureFromSeconds = AcquisitionParameters.DefaultCaptureFromSeconds;
    private double _captureDurationSeconds;
    private double _captureTotalSeconds;
    /// <summary>Sujeto para el que se eligió el tipo: al cambiar a otro hay que volver a elegirlo.</summary>
    private string? _typeSubject;

    public CaptureViewModel(
        RecordSessionUseCase recordSession,
        IRecordingRepository recordings,
        IDataCatalog catalog,
        IModelStore models,
        IBackendEnvironment environment,
        ISettingsStore settings,
        IDialogService dialogs,
        SignalRingBuffer buffer,
        SystemStatusViewModel status)
    {
        Title = "Capturar";
        _recordSession = recordSession;
        _recordings = recordings;
        _catalog = catalog;
        _models = models;
        _environment = environment;
        _settings = settings;
        _dialogs = dialogs;
        _buffer = buffer;
        Status = status;
        Status.PropertyChanged += OnStatusChanged;

        Channels =
        [
            new ChannelViewModel("A", "Channel1Brush"),
            new ChannelViewModel("B", "Channel2Brush"),
            new ChannelViewModel("C", "Channel3Brush"),
            new ChannelViewModel("D", "Channel4Brush"),
        ];

        _executablePath = settings.Load().CaptureExecutablePath;
        _refreshTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) => RefreshLive(), Dispatcher.CurrentDispatcher);

        ReloadSubjects(keep: null);
        ReloadMovements();
        UpdateRecordingCounters(CaptureSnapshot.Empty, TimeSpan.Zero);
        UpdateDurationTexts();
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
    public ObservableCollection<MovementClass> Movements { get; } = [];
    public IReadOnlyList<ModelType> ModelTypes => ModelType.All;
    public ObservableCollection<string> Subjects { get; } = [];
    public IReadOnlyList<ChannelViewModel> Channels { get; }
    public ObservableCollection<RecordingItemViewModel> RecentRecordings { get; } = [];

    // ---- Configuración de sesión ----
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(FileNamePreview))] private MovementClass? _selectedMovement;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ModelTypeHint))] private ModelType? _selectedModelType;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasSelectedSubject))] private string? _selectedSubject;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(FileNamePreview))] private string _sessionNumberText = "1";
    [ObservableProperty] private string _durationText = "25";
    [ObservableProperty] private string _durationHint = "";
    [ObservableProperty] private string _notes = "";
    [ObservableProperty] private string _executablePath;
    [ObservableProperty] private string _outputFolder = "";

    // ---- Añadir movimiento o sujeto ----
    [ObservableProperty] private bool _isAddingMovement;
    [ObservableProperty] private string _newMovementName = "";
    [ObservableProperty] private string _movementWarning = "";
    [ObservableProperty] private bool _isAddingSubject;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(NewSubjectPreview))] private string _newSubjectName = "";
    [ObservableProperty] private string _subjectWarning = "";

    public bool HasSubjects => Subjects.Count > 0;
    public bool ShowNoSubjectsHint => !HasSubjects && !IsAddingSubject;
    public bool HasSelectedSubject => SelectedSubject is not null;
    public string ModelTypeHint => SelectedSubject is null
        ? "Elige o añade primero el sujeto."
        : SelectedModelType is null
            ? "Elige MI o M antes de capturar: se creará su carpeta dentro de la del sujeto."
            : $"Se guarda en {ToDisplayPath(_catalog.TypeDirectory(SelectedSubject, SelectedModelType))}";
    public string NewSubjectPreview => CatalogNames.Normalize(NewSubjectName) is { Length: > 0 } name
        ? $"Se creará la carpeta {ToDisplayPath(_catalog.SubjectDirectory(name))}"
        : "Se creará una carpeta con su nombre; el tipo MI o M se elige después.";
    public string DiscardedSecondsText { get; } = $"{AcquisitionParameters.DefaultCaptureFromSeconds:0} s";

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
    [ObservableProperty] private bool _isCapturing;
    [ObservableProperty] private string _elapsedText = "00:00";
    [ObservableProperty] private string _durationLabel = "de 00:25";
    [ObservableProperty] private string _validSamplesText = "0";
    [ObservableProperty] private string _discardedText = "0";
    [ObservableProperty] private string _recordingState = "Lista para grabar";
    [ObservableProperty] private string _recordingFile = "";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private bool _isError;
    [ObservableProperty] private string _viewAllText = "Ver todas →";

    public override void OnNavigatedTo()
    {
        // Las carpetas pueden haber cambiado fuera de la aplicación.
        if (IsCapturing) return;
        ReloadSubjects(keep: SelectedSubject);
        ReloadMovements();
    }

    partial void OnSelectedMovementChanged(MovementClass? value)
    {
        CancelAddMovement();
        UpdateOutputFolder();
        SuggestSessionNumber();
    }

    partial void OnSelectedSubjectChanged(string? value)
    {
        // Al recargar la lista el sujeto pasa por null y vuelve: solo con otro sujeto se pide elegir el tipo de nuevo.
        if (value is not null && !string.Equals(value, _typeSubject, StringComparison.CurrentCultureIgnoreCase))
            SelectedModelType = null;
        OnPropertyChanged(nameof(ModelTypeHint));
        UpdateOutputFolder();
        SuggestSessionNumber();
    }

    partial void OnSelectedModelTypeChanged(ModelType? value)
    {
        if (value is not null && SelectedSubject is { } subject)
        {
            _typeSubject = subject;
            if (!_catalog.HasType(subject, value))
            {
                try
                {
                    _catalog.EnsureType(subject, value);
                    ShowMessage($"Carpeta {ToDisplayPath(_catalog.TypeDirectory(subject, value))} creada con sus carpetas de movimientos y modelos.");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    ShowMessage($"No se pudo crear la carpeta {value.Folder} de {subject}: {ex.Message}", isError: true);
                }
            }
        }
        UpdateOutputFolder();
        SuggestSessionNumber();
    }

    private void UpdateOutputFolder() =>
        OutputFolder = SelectedMovement is not null && SelectedSubject is not null && SelectedModelType is not null
            ? ToDisplayPath(_catalog.MovementDirectory(SelectedSubject, SelectedModelType, SelectedMovement))
            : "";

    partial void OnDurationTextChanged(string value) => UpdateDurationTexts();

    partial void OnExecutablePathChanged(string value) =>
        _settings.Save(_settings.Load() with { ExecutablePath = value });

    partial void OnSessionNumberTextChanged(string value)
    {
        if (!IsCapturing) RecordingFile = FileNamePreview;
    }

    partial void OnIsCapturingChanged(bool value) => Status.IsCapturing = value;

    partial void OnNewMovementNameChanged(string value) => MovementWarning = "";

    partial void OnNewSubjectNameChanged(string value) => SubjectWarning = "";

    partial void OnIsAddingSubjectChanged(bool value) => OnPropertyChanged(nameof(ShowNoSubjectsHint));

    private void OnStatusChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SystemStatusViewModel.IsTraining) or nameof(SystemStatusViewModel.IsPredicting))
            ToggleCaptureCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void StartAddMovement()
    {
        if (SelectedSubject is null)
        {
            ShowMessage("Elige o añade primero el sujeto: los movimientos se guardan en su carpeta.", isError: true);
            return;
        }
        if (SelectedModelType is null)
        {
            ShowMessage("Elige primero MI o M: el movimiento se guarda en la carpeta de ese tipo.", isError: true);
            return;
        }
        NewMovementName = "";
        MovementWarning = "";
        IsAddingMovement = true;
    }

    [RelayCommand]
    private void ConfirmAddMovement()
    {
        if (SelectedSubject is null || SelectedModelType is null) return;
        var result = _catalog.AddMovement(SelectedSubject, SelectedModelType, NewMovementName);
        if (!result.Succeeded)
        {
            MovementWarning = result.AlreadyExists
                ? $"{result.Error} Elígelo en la lista o escribe otro nombre."
                : result.Error!;
            return;
        }

        ReloadMovements(select: result.Value);
        ShowMessage($"Movimiento «{result.Value!.DisplayName}» añadido en {OutputFolder}.");
    }

    [RelayCommand]
    private void CancelAddMovement()
    {
        IsAddingMovement = false;
        MovementWarning = "";
    }

    [RelayCommand]
    private void StartAddSubject()
    {
        NewSubjectName = "";
        SubjectWarning = "";
        IsAddingSubject = true;
    }

    [RelayCommand]
    private void ConfirmAddSubject()
    {
        var result = _catalog.AddSubject(NewSubjectName);
        if (!result.Succeeded)
        {
            SubjectWarning = result.AlreadyExists
                ? $"{result.Error} Elígelo en la lista o escribe otro nombre."
                : result.Error!;
            return;
        }

        ReloadSubjects(keep: result.Value);
        IsAddingSubject = false;
        ShowMessage($"Sujeto «{result.Value}» creado en {ToDisplayPath(_catalog.SubjectDirectory(result.Value!))}. Elige MI o M para crear la carpeta del tipo.");
    }

    [RelayCommand]
    private void CancelAddSubject()
    {
        IsAddingSubject = false;
        SubjectWarning = "";
    }

    [RelayCommand]
    private void BrowseExecutable()
    {
        if (_dialogs.PickExecutable(ExecutablePath) is { } path)
            ExecutablePath = path;
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

        CancelAddMovement();
        CancelAddSubject();
        _captureFromSeconds = plan.CaptureFromSeconds;
        _captureDurationSeconds = plan.DurationSeconds;
        _captureTotalSeconds = plan.TotalSeconds;
        _buffer.Clear();
        _statistics.Reset();
        _captureCts = new CancellationTokenSource();
        IsCapturing = true;
        Status.IsHeadsetStreaming = false;
        RecordingState = "Iniciando";
        RecordingFile = plan.FileName;
        DurationLabel = $"de {FormatTime(TimeSpan.FromSeconds(plan.DurationSeconds))}";
        ShowMessage($"Iniciando {Path.GetFileName(plan.ExecutablePath)}: {plan.CaptureFromSeconds:0} s de estabilización sin guardar " +
                    $"y {plan.DurationSeconds.ToString("0.#", Es)} s de grabación (total {FormatTime(TimeSpan.FromSeconds(plan.TotalSeconds))}).");
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
                ShowMessage($"{recording.FileName} ({plan.SubjectId}): {recording.ValidSamples.ToString("N0", Es)} muestras válidas y {recording.Windows} ventanas para el dataset.");
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

    private bool CanToggleCapture() => !Status.IsTraining && !Status.IsPredicting;

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

        // El tiempo mostrado es el guardado: los segundos de estabilización van aparte.
        var elapsed = _clock.Elapsed.TotalSeconds;
        if (IsCapturing)
        {
            RecordingState = elapsed < _captureFromSeconds
                ? $"Estabilizando, {Math.Ceiling(_captureFromSeconds - elapsed):0} s sin guardar ·"
                : "Grabando";
        }
        var recorded = Math.Clamp(elapsed - _captureFromSeconds, 0, _captureDurationSeconds);
        UpdateRecordingCounters(_statistics.Snapshot(), TimeSpan.FromSeconds(recorded));
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

    private void UpdateDurationTexts()
    {
        var discard = AcquisitionParameters.DefaultCaptureFromSeconds;
        if (ParseDuration() is { } duration)
        {
            DurationHint = $"Se graban {duration.ToString("0.#", Es)} s. Aparte, el capturador corre {discard:0} s antes " +
                           $"para estabilizar la señal, que no se guardan: total {FormatTime(TimeSpan.FromSeconds(discard + duration))}.";
            if (!IsCapturing) DurationLabel = $"de {FormatTime(TimeSpan.FromSeconds(duration))}";
        }
        else
        {
            DurationHint = $"Segundos que se guardan. Aparte se suman {discard:0} s iniciales de estabilización que no se guardan.";
            if (!IsCapturing) DurationLabel = "de 00:00";
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
        Status.RefreshModel(_models.GetAllModels().FirstOrDefault()?.FilePath);
    }

    private void ReloadMovements(MovementClass? select = null)
    {
        var label = select?.Label ?? SelectedMovement?.Label;
        Movements.Clear();
        try
        {
            foreach (var movement in _catalog.GetMovements())
                Movements.Add(movement);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowMessage($"No se pudieron leer las carpetas de {_environment.DataRoot}: {ex.Message}", isError: true);
        }
        SelectedMovement = Movements.FirstOrDefault(m => m.Label == label) ?? Movements.FirstOrDefault();
    }

    /// <summary>Recarga los sujetos y mantiene <paramref name="keep"/> si sigue existiendo.</summary>
    private void ReloadSubjects(string? keep)
    {
        Subjects.Clear();
        try
        {
            foreach (var subject in _catalog.GetSubjects())
                Subjects.Add(subject);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowMessage($"No se pudieron leer los sujetos de {_environment.DataRoot}: {ex.Message}", isError: true);
        }
        OnPropertyChanged(nameof(HasSubjects));
        OnPropertyChanged(nameof(ShowNoSubjectsHint));

        var subjectToSelect = Subjects.FirstOrDefault(s => string.Equals(s, keep, StringComparison.CurrentCultureIgnoreCase))
                              ?? Subjects.FirstOrDefault();
        // Forzar la notificación aunque el nombre no cambie: la carpeta pudo haberse creado o cambiado fuera.
        SelectedSubject = null;
        SelectedSubject = subjectToSelect;
    }

    private void SuggestSessionNumber()
    {
        if (SelectedMovement is null || SelectedSubject is null || SelectedModelType is null || IsCapturing) return;
        var directory = _catalog.MovementDirectory(SelectedSubject, SelectedModelType, SelectedMovement);
        SessionNumberText = _recordings.NextSessionNumber(SelectedMovement, directory).ToString(CultureInfo.InvariantCulture);
        RecordingFile = FileNamePreview;
    }

    private RecordingPlan? BuildPlan()
    {
        if (SelectedSubject is null)
        {
            ShowMessage("Elige el sujeto o añade uno nuevo con «+».", isError: true);
            return null;
        }
        if (SelectedModelType is not { } type)
        {
            ShowMessage("Elige MI o M antes de capturar.", isError: true);
            return null;
        }
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
            ShowMessage("La duración debe ser un número de segundos mayor que cero.", isError: true);
            return null;
        }

        return new RecordingPlan(
            SelectedSubject, SelectedMovement, session, duration,
            string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
            ExecutablePath.Trim().Trim('"'),
            _catalog.MovementDirectory(SelectedSubject, type, SelectedMovement));
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
