using CommunityToolkit.Mvvm.ComponentModel;

namespace DigitalBrain.UI.ViewModels;

/// <summary>Estado visible de un canal: traza, amplitud pico y porcentaje de muestras válidas.</summary>
public partial class ChannelViewModel(string name, string brushKey) : ObservableObject
{
    public string Name { get; } = name;
    public string Title => $"CH-{Name}";
    public string ValidTitle => $"VÁLIDAS CH-{Name}";
    /// <summary>Clave del recurso de color del canal (Channel1Brush…Channel4Brush).</summary>
    public string BrushKey { get; } = brushKey;

    [ObservableProperty] private IReadOnlyList<double>? _samples;
    [ObservableProperty] private double _range = 100;
    [ObservableProperty] private string _rangeText = "±0 µV";
    /// <summary>Fracción 0–1 de muestras sin saturar en este canal.</summary>
    [ObservableProperty] private double _validRatio;
    [ObservableProperty] private string _validText = "—";
}
