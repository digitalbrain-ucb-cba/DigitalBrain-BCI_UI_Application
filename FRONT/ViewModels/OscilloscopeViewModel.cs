using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DigitalBrain.UI.Controls;
using DigitalBrain.UI.Models;
using DigitalBrain.UI.Models.Dsp;
using DigitalBrain.UI.Services.Dialogs;
using DigitalBrain.UI.UseCases.Ports;

namespace DigitalBrain.UI.ViewModels;

/// <summary>Opción de una lista desplegable o segmentada: se ve <see cref="Label"/> y se usa <see cref="Value"/>.</summary>
public sealed record ScopeOption<T>(string Label, T Value)
{
    // ComboStyle dibuja el elemento elegido con ToString().
    public override string ToString() => Label;
}

public enum SignalSourceKind { Live, Recording }

public enum ScopeMode { Sweep, Scroll, Trigger }

/// <summary>
/// Pantalla «Osciloscopio»: muestra la señal del casco (o de una grabación) como el «Signal Tester» del fabricante.
/// <list type="bullet">
/// <item>Las muestras llegan en un hilo de fondo a una cola; un temporizador de ~30 FPS las filtra (pasa altos, notch y pasa bajos),
/// las guarda en <see cref="SampleHistory"/>, busca pulsos y dibuja.</item>
/// <item>Modos: barrido (la traza avanza y reescribe como un osciloscopio analógico), desplazamiento (la señal corre hacia la izquierda)
/// y disparo (la pantalla se congela centrada en cada pulso detectado, con un 25 % de pre-disparo).</item>
/// <item>Espectro: estimador de Welch (FFT con ventana de Hann, segmentos de ~2 s solapados al 50 % sobre los últimos 4 s),
/// dibujado por bandas; potencia relativa por banda, frecuencia dominante y amplitud de la red eléctrica en la señal sin filtrar.</item>
/// </list>
/// </summary>
public partial class OscilloscopeViewModel : BaseViewModel
{
    private const double HistorySeconds = 30;
    private const double SpectrumSeconds = 4;
    private const double MetricsSeconds = 2;
    private const double PreTriggerFraction = 0.25;
    private const double PulseRefractorySeconds = 0.3;
    private const double MaxSpectrumFrequency = 60;
    private const int SpectrumEveryTicks = 4;
    private const int MaxStoredPulses = 400;
    private static readonly double[] AutoScaleSteps = [1, 2, 5, 10, 20, 25, 50, 100, 200, 250, 500, 1000];
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    private readonly ISignalMonitorGateway _monitor;
    private readonly IRecordingPlayback _playback;
    private readonly ISettingsStore _settings;
    private readonly IBackendEnvironment _environment;
    private readonly IDialogService _dialogs;
    private readonly DispatcherTimer _timer;
    private readonly ConcurrentQueue<SignalSample> _incoming = new();
    private readonly List<PulseEvent> _pulses = [];
    private readonly int[] _pulseCounts = new int[SampleHistory.Channels];
    private readonly long[] _saturatedCounts = new long[SampleHistory.Channels];
    private readonly double[] _raw = new double[SampleHistory.Channels];
    private readonly double[] _filtered = new double[SampleHistory.Channels];
    private readonly Stopwatch _clock = new();

    private CancellationTokenSource? _runCts;
    private int _session;
    private double _sampleRate = AcquisitionParameters.SampleRateHz;
    private SampleHistory _history = new(1);
    private SignalConditioner[] _conditioners = [];
    private PulseDetector[] _detectors = [];
    private WelchSpectrum? _welch;
    private long? _pendingTrigger;
    private ScopeFrame? _triggerFrame;
    private DateTime? _lastTriggerAt;
    private int _triggerCount;
    private int _tick;
    private double _spectrumScale;
    private int _spectrumShrinkTicks;
    private double _autoScale = 25;
    private DateTime _lastAutoScale;
    private long _rateMarkSamples;
    private TimeSpan _rateMarkTime;

    public OscilloscopeViewModel(
        ISignalMonitorGateway monitor,
        IRecordingPlayback playback,
        ISettingsStore settings,
        IBackendEnvironment environment,
        IDialogService dialogs,
        SystemStatusViewModel status)
    {
        Title = "Osciloscopio";
        _monitor = monitor;
        _playback = playback;
        _settings = settings;
        _environment = environment;
        _dialogs = dialogs;
        Status = status;
        Status.PropertyChanged += OnStatusChanged;

        Channels =
        [
            new ScopeChannelViewModel(0, "A", "Channel1Brush"),
            new ScopeChannelViewModel(1, "B", "Channel2Brush"),
            new ScopeChannelViewModel(2, "C", "Channel3Brush"),
            new ScopeChannelViewModel(3, "D", "Channel4Brush"),
        ];
        foreach (var channel in Channels)
            channel.PropertyChanged += OnChannelChanged;

        _selectedSource = Sources[0];
        _selectedMode = Modes[0];
        _selectedTimeBase = TimeBases[2];
        _selectedScale = Scales[0];
        _selectedHighPass = HighPassOptions[1];
        _selectedNotch = NotchOptions[1];
        _selectedLowPass = LowPassOptions[2];
        _selectedTriggerChannel = TriggerChannels[0];
        _selectedSensitivity = Sensitivities[1];

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) => OnTick(), Dispatcher.CurrentDispatcher);
        UpdateSourceTexts();
    }

    public SystemStatusViewModel Status { get; }
    public IReadOnlyList<ScopeChannelViewModel> Channels { get; }
    public IReadOnlyList<EegBand> Bands => EegBand.All;

    public IReadOnlyList<ScopeOption<SignalSourceKind>> Sources { get; } =
    [
        new("Casco en vivo", SignalSourceKind.Live),
        new("Grabación", SignalSourceKind.Recording),
    ];

    public IReadOnlyList<ScopeOption<ScopeMode>> Modes { get; } =
    [
        new("Barrido", ScopeMode.Sweep),
        new("Rodante", ScopeMode.Scroll),
        new("Disparo", ScopeMode.Trigger),
    ];

    public IReadOnlyList<ScopeOption<double>> TimeBases { get; } =
        new[] { 0.1, 0.2, 0.5, 1, 2 }.Select(s => new ScopeOption<double>($"{s.ToString("0.#", Es)} s/div", s)).ToArray();

    /// <summary>µV por división; 0 = automática.</summary>
    public IReadOnlyList<ScopeOption<double>> Scales { get; } =
        new[] { 0.0, 5, 10, 25, 50, 100, 250, 500 }
            .Select(v => new ScopeOption<double>(v == 0 ? "Auto" : $"{v:0} µV/div", v)).ToArray();

    public IReadOnlyList<ScopeOption<double?>> HighPassOptions { get; } =
    [
        new("Sin filtro", null), new("0,5 Hz", 0.5), new("1 Hz", 1), new("5 Hz", 5),
    ];

    public IReadOnlyList<ScopeOption<double?>> NotchOptions { get; } =
    [
        new("Sin notch", null), new("50 Hz", 50), new("60 Hz", 60),
    ];

    public IReadOnlyList<ScopeOption<double?>> LowPassOptions { get; } =
    [
        new("Sin filtro", null), new("30 Hz", 30), new("45 Hz", 45),
    ];

    /// <summary>Canal que dispara; -1 = cualquiera.</summary>
    public IReadOnlyList<ScopeOption<int>> TriggerChannels { get; } =
        new[] { new ScopeOption<int>("Cualquier canal", -1) }
            .Concat(AcquisitionParameters.ChannelNames.Select((name, i) => new ScopeOption<int>($"CH-{name}", i))).ToArray();

    /// <summary>k del umbral mediana ± k·σ.</summary>
    public IReadOnlyList<ScopeOption<double>> Sensitivities { get; } =
    [
        new("Alta · 4σ", 4), new("Media · 6σ", 6), new("Baja · 10σ", 10),
    ];

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsRecordingSource))] private ScopeOption<SignalSourceKind> _selectedSource;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsTriggerMode))] private ScopeOption<ScopeMode> _selectedMode;
    [ObservableProperty] private ScopeOption<double> _selectedTimeBase;
    [ObservableProperty] private ScopeOption<double> _selectedScale;
    [ObservableProperty] private ScopeOption<double?> _selectedHighPass;
    [ObservableProperty] private ScopeOption<double?> _selectedNotch;
    [ObservableProperty] private ScopeOption<double?> _selectedLowPass;
    [ObservableProperty] private ScopeOption<int> _selectedTriggerChannel;
    [ObservableProperty] private ScopeOption<double> _selectedSensitivity;

    [ObservableProperty] private string _recordingPath = "";
    [ObservableProperty] private string _executablePath = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ToggleText))] private bool _isRunning;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ToggleText))] private bool _isStopping;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(PauseText))] private bool _isPaused;
    [ObservableProperty] private ScopeFrame? _scopeFrame;
    [ObservableProperty] private SpectrumFrame? _spectrumAB;
    [ObservableProperty] private SpectrumFrame? _spectrumCD;
    [ObservableProperty] private string _scaleText = "";
    [ObservableProperty] private string _overlayText = "Pulsa «Iniciar» para ver la señal.";
    [ObservableProperty] private string _triggerStatus = "";
    [ObservableProperty] private string _rateText = "—";
    [ObservableProperty] private string _elapsedText = "00:00";
    [ObservableProperty] private string _pulseTotalText = "0";
    [ObservableProperty] private string _sourceHint = "";
    [ObservableProperty] private string _filterSummary = "";
    [ObservableProperty] private string _resolutionText = "";
    [ObservableProperty] private string _deviceStatus = "";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private bool _isError;

    public bool IsRecordingSource => SelectedSource.Value == SignalSourceKind.Recording;
    public bool IsTriggerMode => SelectedMode.Value == ScopeMode.Trigger;
    public string ToggleText => IsStopping ? "Deteniendo…" : IsRunning ? "Detener" : "Iniciar";
    public string PauseText => IsPaused ? "Reanudar" : "Congelar";

    public override void OnNavigatedTo()
    {
        ExecutablePath = _settings.Load().CaptureExecutablePath;
        UpdateSourceTexts();
    }

    // ---------------------------------------------------------------- Inicio y parada

    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanToggleRun))]
    private async Task ToggleRunAsync()
    {
        if (IsRunning)
        {
            if (!IsStopping)
            {
                IsStopping = true;
                ShowMessage(IsRecordingSource ? "Deteniendo la reproducción…" : "Cerrando el capturador…");
                Stop();
            }
            return;
        }

        var source = SelectedSource.Value;
        ExecutablePath = _settings.Load().CaptureExecutablePath;
        if (source == SignalSourceKind.Live)
        {
            if (Status.IsCapturing || Status.IsPredicting)
            {
                ShowMessage("El casco está ocupado: detén la captura o la predicción antes de abrir el osciloscopio en vivo.", isError: true);
                return;
            }
            if (!File.Exists(ExecutablePath))
            {
                ShowMessage("No se encontró el capturador en este equipo. Su ruta se configura en la pantalla Capturar.", isError: true);
                return;
            }
        }
        else if (!File.Exists(RecordingPath))
        {
            ShowMessage("Elige primero una grabación .txt con «···».", isError: true);
            return;
        }

        ResetSession();
        var session = _session;
        _runCts = new CancellationTokenSource();
        IsRunning = true;
        IsPaused = false;
        if (source == SignalSourceKind.Live) Status.IsMonitoring = true;
        DeviceStatus = "";
        OverlayText = source == SignalSourceKind.Live ? "Conectando con el casco…" : "Cargando la grabación…";
        ShowMessage(source == SignalSourceKind.Live ? $"Iniciando {Path.GetFileName(ExecutablePath)} sin grabar." : "");
        _clock.Restart();
        _timer.Start();

        try
        {
            void OnSample(SignalSample sample)
            {
                if (session == _session) _incoming.Enqueue(sample);
            }

            var outcome = source == SignalSourceKind.Live
                ? await _monitor.RunAsync(ExecutablePath, OnSample, OnDeviceLog, _runCts.Token)
                : await _playback.PlayAsync(RecordingPath, _sampleRate, loop: true, OnSample, _runCts.Token);

            if (outcome.Cancelled)
                ShowMessage(source == SignalSourceKind.Live ? "Osciloscopio detenido; el casco quedó libre." : "Reproducción detenida.");
            else if (outcome.Error is { } error)
                ShowMessage(error, isError: true);
            else
                ShowMessage("La señal terminó.");
        }
        catch (Exception ex)
        {
            ShowMessage($"Error en el osciloscopio: {ex.Message}", isError: true);
        }
        finally
        {
            OnTick(); // procesa lo que quedara en la cola
            _timer.Stop();
            _clock.Stop();
            IsRunning = false;
            IsStopping = false;
            IsPaused = false;
            Status.IsMonitoring = false;
            Status.IsHeadsetStreaming = false;
            _runCts.Dispose();
            _runCts = null;
            if (_history.Total == 0) OverlayText = "No llegó ninguna muestra.";
            else if (ScopeFrame is null) OverlayText = IsTriggerMode ? "No se detectó ningún pulso." : "";
        }
    }

    private bool CanToggleRun() => IsRunning || IsRecordingSource || (!Status.IsCapturing && !Status.IsPredicting);

    /// <summary>Detiene la señal en curso, si la hay; también se llama al cerrar la aplicación.</summary>
    public void Stop() => _runCts?.Cancel();

    [RelayCommand]
    private void TogglePause()
    {
        if (!IsRunning && !IsPaused) return;
        IsPaused = !IsPaused;
        if (!IsPaused) Render();
    }

    [RelayCommand]
    private void BrowseRecording()
    {
        var initial = File.Exists(RecordingPath) ? RecordingPath : _environment.DataRoot + Path.DirectorySeparatorChar;
        if (_dialogs.PickRecording(initial) is not { } path) return;
        RecordingPath = path;
        SelectedSource = Sources.First(s => s.Value == SignalSourceKind.Recording);
    }

    /// <summary>Vuelve a esperar el siguiente pulso en modo disparo.</summary>
    [RelayCommand]
    private void RearmTrigger()
    {
        _pendingTrigger = null;
        _triggerFrame = null;
        ScopeFrame = null;
        OverlayText = IsRunning ? "Esperando un pulso…" : "";
        UpdateTriggerStatus();
    }

    private void ResetSession()
    {
        _session++;
        while (_incoming.TryDequeue(out _)) { }
        _sampleRate = AcquisitionParameters.SampleRateHz;
        _history = new SampleHistory((int)Math.Ceiling(_sampleRate * HistorySeconds));
        _welch = new WelchSpectrum(_sampleRate, Fft.NextPowerOfTwo((int)Math.Round(_sampleRate * 2)));
        BuildFilters();
        _pulses.Clear();
        Array.Clear(_pulseCounts);
        Array.Clear(_saturatedCounts);
        _pendingTrigger = null;
        _triggerFrame = null;
        _lastTriggerAt = null;
        _triggerCount = 0;
        _tick = 0;
        _spectrumScale = 0;
        _rateMarkSamples = 0;
        _rateMarkTime = TimeSpan.Zero;
        ScopeFrame = null;
        SpectrumAB = SpectrumCD = null;
        RateText = "—";
        ElapsedText = "00:00";
        PulseTotalText = "0";
        foreach (var channel in Channels) channel.Reset();
        UpdateFilterSummary();
        ResolutionText = $"Resolución {_welch.FrequencyStep.ToString("0.##", Es)} Hz · Welch {_welch.SegmentLength} muestras, Hann, 50 % solape";
    }

    private void BuildFilters()
    {
        var settings = new FilterSettings(SelectedHighPass.Value, SelectedNotch.Value, SelectedLowPass.Value);
        _conditioners = Enumerable.Range(0, SampleHistory.Channels).Select(_ => new SignalConditioner(_sampleRate, settings)).ToArray();
        _detectors = Enumerable.Range(0, SampleHistory.Channels)
            .Select(c => new PulseDetector(c, (int)Math.Round(_sampleRate * 2), (int)Math.Round(_sampleRate * PulseRefractorySeconds))
            {
                Sensitivity = SelectedSensitivity.Value,
            })
            .ToArray();
    }

    // ---------------------------------------------------------------- Procesado

    private void OnTick()
    {
        int received = Drain();
        if (received > 0 && Status.IsMonitoring) Status.IsHeadsetStreaming = true;
        if (_history.Total > 0 && OverlayText.Length > 0 && !IsTriggerMode) OverlayText = "";

        UpdateRate();
        if (IsPaused) return;
        Render();
        if (++_tick % SpectrumEveryTicks == 0) UpdateSpectrumAndMetrics();
    }

    /// <summary>Filtra y guarda las muestras recibidas y busca pulsos en ellas.</summary>
    private int Drain()
    {
        int count = 0;
        while (_incoming.TryDequeue(out var sample))
        {
            _raw[0] = sample.Channel1;
            _raw[1] = sample.Channel2;
            _raw[2] = sample.Channel3;
            _raw[3] = sample.Channel4;
            bool anySaturated = false;
            for (int c = 0; c < SampleHistory.Channels; c++)
            {
                bool saturated = SignalSample.CheckSaturation(_raw[c]);
                if (saturated)
                {
                    anySaturated = true;
                    _saturatedCounts[c]++;
                }
                _filtered[c] = _conditioners[c].Process(_raw[c], saturated);
            }
            long index = _history.Append(_raw, _filtered, anySaturated);
            for (int c = 0; c < SampleHistory.Channels; c++)
            {
                if (_detectors[c].Process(index, _filtered[c], SignalSample.CheckSaturation(_raw[c])) is { } pulse)
                    OnPulse(pulse);
            }
            count++;
        }
        if (count > 0)
        {
            foreach (var detector in _detectors) detector.UpdateThreshold();
        }
        return count;
    }

    private void OnPulse(PulseEvent pulse)
    {
        _pulses.Add(pulse);
        if (_pulses.Count > MaxStoredPulses) _pulses.RemoveRange(0, _pulses.Count - MaxStoredPulses);
        _pulseCounts[pulse.Channel]++;

        if (IsTriggerMode && _pendingTrigger is null && Channels[pulse.Channel].IsVisible &&
            (SelectedTriggerChannel.Value < 0 || SelectedTriggerChannel.Value == pulse.Channel))
        {
            _pendingTrigger = pulse.SampleIndex;
        }
    }

    /// <summary>Vuelve a filtrar toda la historia (al cambiar un filtro) y a buscar pulsos en ella.</summary>
    private void Refilter()
    {
        if (_conditioners.Length == 0) return;
        BuildFilters();
        _pulses.Clear();
        Array.Clear(_pulseCounts);
        _pendingTrigger = null;
        int thresholdEvery = Math.Max(1, (int)(_sampleRate / 4));
        for (long i = _history.Oldest; i < _history.Total; i++)
        {
            for (int c = 0; c < SampleHistory.Channels; c++)
            {
                double raw = _history.Raw(c, i);
                bool saturated = SignalSample.CheckSaturation(raw);
                double value = _conditioners[c].Process(raw, saturated);
                _history.SetFiltered(c, i, value);
                if (_detectors[c].Process(i, value, saturated) is { } pulse)
                {
                    _pulses.Add(pulse);
                    _pulseCounts[c]++;
                }
            }
            if ((i - _history.Oldest) % thresholdEvery == 0)
            {
                foreach (var detector in _detectors) detector.UpdateThreshold();
            }
        }
        foreach (var detector in _detectors) detector.UpdateThreshold();
        RefreshDisplay();
    }

    // ---------------------------------------------------------------- Osciloscopio

    private int FrameLength => Math.Max(2, (int)Math.Round(SelectedTimeBase.Value * OscilloscopeDisplay.Divisions * _sampleRate));

    private void Render()
    {
        if (_history.Total == 0) return;
        int length = FrameLength;
        long total = _history.Total;
        ScopeFrame? frame = null;

        switch (SelectedMode.Value)
        {
            case ScopeMode.Sweep:
            {
                // Un pequeño hueco delante del cursor separa la traza nueva de la anterior, como en un osciloscopio analógico.
                int gap = Math.Max(2, length / 50);
                long first = Math.Max(total - length + gap, 0);
                frame = BuildFrame(first, total, length, i => (int)(i % length), (int)(total % length), null, 0);
                break;
            }
            case ScopeMode.Scroll:
            {
                long first = total - length;
                frame = BuildFrame(first, total, length, i => (int)(i - first), null, null, -length / _sampleRate);
                break;
            }
            case ScopeMode.Trigger:
            {
                int pre = (int)(length * PreTriggerFraction);
                if (_pendingTrigger is { } trigger && total >= trigger + (length - pre))
                {
                    long first = trigger - pre;
                    _triggerFrame = BuildFrame(first, first + length, length, i => (int)(i - first), null, pre, -pre / _sampleRate);
                    _pendingTrigger = null;
                    _lastTriggerAt = DateTime.Now;
                    _triggerCount++;
                }
                frame = _triggerFrame;
                OverlayText = _triggerFrame is null ? (IsRunning ? "Esperando un pulso…" : "") : "";
                UpdateTriggerStatus();
                break;
            }
        }

        if (frame is not null) UpdateAutoScale(frame);
        // La escala se aplica al dibujar: un cuadro congelado por disparo también sigue a la escala automática o elegida.
        ScopeFrame = frame is null ? null : frame with { MicroVoltsPerDivision = CurrentScale };
        ScaleText = $"{FormatScale(CurrentScale)}/div · {SelectedTimeBase.Value.ToString("0.#", Es)} s/div";
    }

    private double CurrentScale => SelectedScale.Value > 0 ? SelectedScale.Value : _autoScale;

    private ScopeFrame BuildFrame(long first, long lastExclusive, int length, Func<long, int> slotOf, int? cursor, int? trigger, double startSeconds)
    {
        var visible = Channels.Where(c => c.IsVisible).Select(c => c.Index).ToArray();
        var values = visible.Select(_ => Enumerable.Repeat(double.NaN, length).ToArray()).ToArray();
        var saturated = new bool[length];
        long from = Math.Max(first, _history.Oldest), to = Math.Min(lastExclusive, _history.Total);
        for (long i = from; i < to; i++)
        {
            int slot = slotOf(i);
            if (slot < 0 || slot >= length) continue;
            saturated[slot] = _history.IsSaturated(i);
            for (int v = 0; v < visible.Length; v++)
                values[v][slot] = _history.Filtered(visible[v], i);
        }

        var traces = new ScopeTrace[visible.Length];
        for (int v = 0; v < visible.Length; v++)
        {
            int channel = visible[v];
            var pulses = _pulses
                .Where(p => p.Channel == channel && p.SampleIndex >= first && p.SampleIndex < lastExclusive && p.SampleIndex >= from)
                .Select(p => slotOf(p.SampleIndex))
                .ToArray();
            traces[v] = new ScopeTrace(channel, values[v], pulses);
        }
        return new ScopeFrame(traces, saturated, length, cursor, trigger, startSeconds, SelectedTimeBase.Value, CurrentScale);
    }

    /// <summary>
    /// Escala automática: el percentil 99 de |µV| de lo visible debe caber en ±2 divisiones. Sube al instante y baja como mucho
    /// una vez por segundo, para que la escala no salte con cada parpadeo.
    /// </summary>
    private void UpdateAutoScale(ScopeFrame frame)
    {
        if (SelectedScale.Value > 0) return;
        var magnitudes = new List<double>(frame.Length * frame.Traces.Count);
        foreach (var trace in frame.Traces)
        {
            for (int i = 0; i < frame.Length; i++)
            {
                if (!double.IsNaN(trace.Values[i]) && frame.Saturated?[i] != true)
                    magnitudes.Add(Math.Abs(trace.Values[i]));
            }
        }
        if (magnitudes.Count < 8) return;
        magnitudes.Sort();
        double p99 = magnitudes[(int)(magnitudes.Count * 0.99)];
        double needed = p99 / (OscilloscopeDisplay.LaneDivisions / 2.0) * 1.15;
        double target = AutoScaleSteps.FirstOrDefault(step => step >= needed, AutoScaleSteps[^1]);
        var now = DateTime.Now;
        if (target > _autoScale || (target < _autoScale && now - _lastAutoScale > TimeSpan.FromSeconds(1)))
        {
            _autoScale = target;
            _lastAutoScale = now;
        }
    }

    private void UpdateTriggerStatus()
    {
        if (!IsTriggerMode)
        {
            TriggerStatus = "";
            return;
        }
        var channel = SelectedTriggerChannel.Value;
        var thresholds = (channel < 0 ? Enumerable.Range(0, SampleHistory.Channels) : [channel])
            .Where(c => _detectors.Length > c && !double.IsNaN(_detectors[c].Threshold))
            .Select(c => _detectors[c].Threshold)
            .ToArray();
        string level = thresholds.Length == 0 ? "calculando umbral" : $"umbral ±{thresholds.Min():0}–{thresholds.Max():0} µV";
        if (thresholds.Length == 1) level = $"umbral ±{thresholds[0]:0} µV";
        TriggerStatus = _lastTriggerAt is { } at
            ? $"Disparo n.º {_triggerCount} hace {(DateTime.Now - at).TotalSeconds.ToString("0.0", Es)} s · {SelectedTriggerChannel.Label} · {level}"
            : $"Esperando pulso en {SelectedTriggerChannel.Label.ToLower(Es)} · {level}";
    }

    // ---------------------------------------------------------------- Espectro y medidas

    private void UpdateSpectrumAndMetrics()
    {
        if (_welch is null || _history.Total == 0) return;

        int spectrumSamples = (int)Math.Round(_sampleRate * SpectrumSeconds);
        int metricSamples = (int)Math.Round(_sampleRate * MetricsSeconds);
        var buffer = new double[Math.Max(spectrumSamples, metricSamples)];
        var amplitudes = new double[SampleHistory.Channels][];
        var peaks = new double[SampleHistory.Channels];
        double mains = SelectedNotch.Value ?? 50;

        for (int c = 0; c < SampleHistory.Channels; c++)
        {
            var channel = Channels[c];
            peaks[c] = double.NaN;

            int n = _history.CopyLatestFiltered(c, buffer.AsSpan(0, spectrumSamples));
            var spectrum = _welch.Compute(buffer.AsSpan(0, n));
            if (spectrum is not null)
            {
                amplitudes[c] = spectrum.Amplitude;
                peaks[c] = spectrum.DominantFrequency;
                var shares = spectrum.RelativeBandPower();
                channel.BandShares = shares;
                channel.BandItems = EegBand.All
                    .Select((band, b) => new BandShareItem(band.Symbol, band.Name, shares[b].ToString("P0", Es), band.BrushKey))
                    .ToArray();
                int dominantBand = Array.IndexOf(shares, shares.Max());
                channel.DominantBandText = spectrum.TotalPower > 0 ? EegBand.All[dominantBand].Name : "";
                channel.DominantText = double.IsNaN(spectrum.DominantFrequency)
                    ? "—"
                    : $"{spectrum.DominantFrequency.ToString("0.0", Es)} Hz";
                channel.MainsText = mains < _sampleRate / 2 ? $"{RawAmplitudeAt(c, mains, spectrumSamples).ToString("0.0", Es)} µV" : "—";
            }

            // Amplitud de los últimos 2 s filtrados.
            int m = _history.CopyLatestFiltered(c, buffer.AsSpan(0, metricSamples));
            if (m > 0)
            {
                var span = buffer.AsSpan(0, m);
                double mean = 0, min = double.PositiveInfinity, max = double.NegativeInfinity;
                foreach (var v in span)
                {
                    mean += v;
                    if (v < min) min = v;
                    if (v > max) max = v;
                }
                mean /= m;
                double sum = 0;
                foreach (var v in span) sum += (v - mean) * (v - mean);
                channel.RmsText = $"{Math.Sqrt(sum / m).ToString("0.0", Es)} µV";
                channel.PeakToPeakText = $"{(max - min).ToString("0", Es)} µV";
            }

            channel.PulseCountText = _pulseCounts[c].ToString("N0", Es);
            var last = _detectors[c].LastPulse;
            channel.LastPulseText = last is null
                ? "Sin pulsos"
                : $"Último {last.Peak.ToString("+0;-0", Es)} µV, hace {((_history.Total - last.SampleIndex) / _sampleRate).ToString("0.0", Es)} s";
            double saturatedRatio = _history.Total == 0 ? 0 : (double)_saturatedCounts[c] / _history.Total;
            channel.SaturationText = saturatedRatio.ToString("P1", Es);
            channel.IsSaturating = RecentlySaturated(c);
        }
        PulseTotalText = _pulseCounts.Sum().ToString("N0", Es);

        // Escala común a los dos espectros: sube al instante, baja poco a poco.
        double maxFrequency = Math.Min(MaxSpectrumFrequency, _sampleRate / 2);
        double target = 0;
        for (int c = 0; c < SampleHistory.Channels; c++)
        {
            if (amplitudes[c] is not { } a || !Channels[c].IsVisible) continue;
            for (int k = 0; k < a.Length; k++)
            {
                double f = k * _welch.FrequencyStep;
                if (f < 1) continue;
                if (f > maxFrequency) break;
                target = Math.Max(target, a[k]);
            }
        }
        // Escala redonda (1, 2, 2,5, 5 × 10ⁿ): sube al instante y baja solo si sobra más de la mitad durante ~2 s.
        target = NiceCeiling(target * 1.1);
        if (target >= _spectrumScale)
        {
            _spectrumScale = target;
            _spectrumShrinkTicks = 0;
        }
        else if (target <= _spectrumScale / 2 && ++_spectrumShrinkTicks >= 16)
        {
            _spectrumScale = target;
            _spectrumShrinkTicks = 0;
        }
        else if (target > _spectrumScale / 2)
        {
            _spectrumShrinkTicks = 0;
        }

        double[]? Visible(int c) => Channels[c].IsVisible ? amplitudes[c] : null;
        double Peak(int c) => Channels[c].IsVisible ? peaks[c] : double.NaN;
        SpectrumAB = new SpectrumFrame(Visible(0), Visible(1), _welch.FrequencyStep, maxFrequency, _spectrumScale, Peak(0), Peak(1));
        SpectrumCD = new SpectrumFrame(Visible(2), Visible(3), _welch.FrequencyStep, maxFrequency, _spectrumScale, Peak(2), Peak(3));
    }

    /// <summary>Amplitud (µV) de la red eléctrica en la señal sin filtrar: indica mal contacto o cables sin apantallar.</summary>
    private double RawAmplitudeAt(int channel, double frequency, int samples)
    {
        if (_welch is null) return 0;
        long count = Math.Min(samples, _history.Total - _history.Oldest);
        var raw = new double[count];
        long start = _history.Total - count;
        for (long i = 0; i < count; i++) raw[i] = _history.Raw(channel, start + i);
        var spectrum = _welch.Compute(raw);
        if (spectrum is null) return 0;
        int k = (int)Math.Round(frequency / spectrum.FrequencyStep);
        // El máximo de los bins vecinos: la frecuencia de la red no cae justo en un bin.
        return Enumerable.Range(k - 1, 3).Where(i => i > 0 && i < spectrum.Amplitude.Length).Select(i => spectrum.Amplitude[i]).DefaultIfEmpty(0).Max();
    }

    private bool RecentlySaturated(int channel)
    {
        long from = Math.Max(_history.Oldest, _history.Total - (long)_sampleRate);
        for (long i = from; i < _history.Total; i++)
        {
            if (SignalSample.CheckSaturation(_history.Raw(channel, i))) return true;
        }
        return false;
    }

    private void UpdateRate()
    {
        var elapsed = _clock.Elapsed;
        ElapsedText = $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";
        // Cada 2 s: las muestras llegan en bloques, y en ventanas más cortas el ritmo medido oscila.
        if (elapsed - _rateMarkTime < TimeSpan.FromSeconds(2)) return;
        double rate = (_history.Total - _rateMarkSamples) / (elapsed - _rateMarkTime).TotalSeconds;
        _rateMarkSamples = _history.Total;
        _rateMarkTime = elapsed;
        RateText = IsRunning ? $"{rate.ToString("0", Es)} muestras/s" : "—";
    }

    private static double NiceCeiling(double value)
    {
        if (value <= 0) return 1;
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
        foreach (var step in new[] { 1, 2, 2.5, 5, 10 })
        {
            if (step * magnitude >= value) return step * magnitude;
        }
        return 10 * magnitude;
    }

    private static string FormatScale(double microVolts) => microVolts >= 1000
        ? $"{(microVolts / 1000).ToString("0.##", Es)} mV"
        : $"{microVolts.ToString("0.#", Es)} µV";

    // ---------------------------------------------------------------- Cambios de opciones

    partial void OnSelectedModeChanged(ScopeOption<ScopeMode> value)
    {
        _pendingTrigger = null;
        _triggerFrame = null;
        ScopeFrame = null;
        OverlayText = value.Value == ScopeMode.Trigger && IsRunning ? "Esperando un pulso…" : "";
        UpdateTriggerStatus();
        RefreshDisplay();
    }

    partial void OnSelectedTimeBaseChanged(ScopeOption<double> value)
    {
        _triggerFrame = null;
        _pendingTrigger = null;
        RefreshDisplay();
    }

    partial void OnSelectedScaleChanged(ScopeOption<double> value) => RefreshDisplay();
    partial void OnSelectedHighPassChanged(ScopeOption<double?> value) => OnFilterChanged();
    partial void OnSelectedNotchChanged(ScopeOption<double?> value) => OnFilterChanged();
    partial void OnSelectedLowPassChanged(ScopeOption<double?> value) => OnFilterChanged();

    partial void OnSelectedTriggerChannelChanged(ScopeOption<int> value)
    {
        _pendingTrigger = null;
        UpdateTriggerStatus();
    }

    /// <summary>Los detectores se crean de nuevo con la sensibilidad elegida y repasan la historia.</summary>
    partial void OnSelectedSensitivityChanged(ScopeOption<double> value) => Refilter();

    partial void OnSelectedSourceChanged(ScopeOption<SignalSourceKind> value)
    {
        UpdateSourceTexts();
        ToggleRunCommand.NotifyCanExecuteChanged();
    }

    partial void OnRecordingPathChanged(string value) => UpdateSourceTexts();

    private void OnFilterChanged()
    {
        UpdateFilterSummary();
        Refilter();
    }

    private void OnChannelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScopeChannelViewModel.IsVisible))
        {
            _triggerFrame = null;
            RefreshDisplay();
        }
    }

    /// <summary>Redibuja con las opciones nuevas aunque la señal esté congelada o detenida.</summary>
    private void RefreshDisplay()
    {
        if (_history.Total == 0) return;
        Render();
        UpdateSpectrumAndMetrics();
    }

    private void UpdateFilterSummary()
    {
        var parts = new List<string>();
        if (SelectedHighPass.Value is { } hp) parts.Add($"pasa altos {hp.ToString("0.#", Es)} Hz");
        if (SelectedNotch.Value is { } notch) parts.Add($"notch {notch:0} Hz");
        if (SelectedLowPass.Value is { } lp) parts.Add($"pasa bajos {lp:0} Hz");
        FilterSummary = parts.Count == 0
            ? "Señal sin filtrar: verás la continua y la deriva de los electrodos."
            : $"Butterworth de 4.º orden: {string.Join(" · ", parts)}.";
    }

    private void UpdateSourceTexts()
    {
        SourceHint = IsRecordingSource
            ? string.IsNullOrWhiteSpace(RecordingPath)
                ? "Elige una captura .txt para revisarla como si llegara del casco."
                : $"Se reproduce {Path.GetFileName(RecordingPath)} a {AcquisitionParameters.SampleRateHz:0} Hz, en bucle."
            : File.Exists(ExecutablePath)
                ? $"Lanza {Path.GetFileName(ExecutablePath)} con el comando monitor del backend: no se guarda nada."
                : "No se encontró el capturador: configura su ruta en Capturar.";
    }

    private void OnStatusChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SystemStatusViewModel.IsCapturing) or nameof(SystemStatusViewModel.IsPredicting))
            ToggleRunCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsRunningChanged(bool value) => ToggleRunCommand.NotifyCanExecuteChanged();

    private void OnDeviceLog(string line) => System.Windows.Application.Current?.Dispatcher.InvokeAsync(() => DeviceStatus = line);

    private void ShowMessage(string text, bool isError = false)
    {
        Message = text;
        IsError = isError;
    }
}
