using CommunityToolkit.Mvvm.ComponentModel;
using DigitalBrain.UI.Models.Dsp;

namespace DigitalBrain.UI.ViewModels;

/// <summary>Porcentaje de potencia de una banda, para la leyenda bajo la barra de bandas.</summary>
public sealed record BandShareItem(string Symbol, string Name, string PercentText, string BrushKey);

/// <summary>Canal del osciloscopio: si se dibuja y sus medidas (amplitud, espectro, bandas, pulsos, saturación).</summary>
public partial class ScopeChannelViewModel(int index, string name, string brushKey) : ObservableObject
{
    public int Index { get; } = index;
    public string Name { get; } = name;
    public string Title => $"CH-{Name}";
    /// <summary>Clave del recurso de color del canal (Channel1Brush…Channel4Brush).</summary>
    public string BrushKey { get; } = brushKey;

    [ObservableProperty] private bool _isVisible = true;
    [ObservableProperty] private string _rmsText = "—";
    [ObservableProperty] private string _peakToPeakText = "—";
    [ObservableProperty] private string _dominantText = "—";
    [ObservableProperty] private string _dominantBandText = "";
    [ObservableProperty] private IReadOnlyList<double>? _bandShares;
    [ObservableProperty] private IReadOnlyList<BandShareItem> _bandItems = EmptyBands;
    [ObservableProperty] private string _mainsText = "—";
    [ObservableProperty] private string _pulseCountText = "0";
    [ObservableProperty] private string _lastPulseText = "Sin pulsos";
    [ObservableProperty] private string _saturationText = "0 %";
    [ObservableProperty] private bool _isSaturating;

    private static readonly IReadOnlyList<BandShareItem> EmptyBands =
        EegBand.All.Select(b => new BandShareItem(b.Symbol, b.Name, "—", b.BrushKey)).ToArray();

    public void Reset()
    {
        RmsText = PeakToPeakText = DominantText = MainsText = "—";
        DominantBandText = "";
        BandShares = null;
        BandItems = EmptyBands;
        PulseCountText = "0";
        LastPulseText = "Sin pulsos";
        SaturationText = "0 %";
        IsSaturating = false;
    }
}
