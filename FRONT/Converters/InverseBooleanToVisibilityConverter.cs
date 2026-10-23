using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DigitalBrain.UI.Converters;

/// <summary>Muestra el elemento cuando el booleano es falso; se usa para alternar entre la lista y el campo «añadir».</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not Visibility.Visible;
}
