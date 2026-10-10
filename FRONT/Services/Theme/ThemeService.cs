using System.Windows;
using DigitalBrain.UI.UseCases.Ports;

namespace DigitalBrain.UI.Services.Theme;

public enum AppTheme { Light, Dark }

public interface IThemeService
{
    AppTheme Current { get; }
    void Apply(AppTheme theme);
}

/// <summary>
/// Cambia el diccionario de colores (<c>Styles/Themes/Light.xaml</c> o <c>Dark.xaml</c>).
/// Las vistas usan <c>DynamicResource</c>, así que el cambio se ve al instante.
/// </summary>
public sealed class ThemeService(ISettingsStore settings) : IThemeService
{
    private ResourceDictionary? _active;

    public AppTheme Current { get; private set; } =
        Enum.TryParse<AppTheme>(settings.Load().Theme, out var saved) ? saved : AppTheme.Light;

    public void Apply(AppTheme theme)
    {
        var resources = System.Windows.Application.Current.Resources.MergedDictionaries;
        var next = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Styles/Themes/{theme}.xaml", UriKind.Absolute),
        };
        if (_active is not null) resources.Remove(_active);
        resources.Insert(0, next);
        _active = next;

        Current = theme;
        settings.Save(settings.Load() with { Theme = theme.ToString() });
    }
}
