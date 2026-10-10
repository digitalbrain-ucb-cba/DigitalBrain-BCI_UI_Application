using System.Globalization;
using System.Windows.Data;

namespace DigitalBrain.UI.Converters;

/// <summary>Invierte un booleano; se usa para bloquear el formulario mientras se graba.</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}
