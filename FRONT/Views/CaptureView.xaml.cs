using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DigitalBrain.UI.Views;

public partial class CaptureView : UserControl
{
    public CaptureView()
    {
        InitializeComponent();
    }

    /// <summary>Al abrir el campo «añadir», el cursor queda listo para escribir el nombre.</summary>
    private void FocusWhenShown(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && sender is TextBox box)
            Dispatcher.BeginInvoke(() => box.Focus(), DispatcherPriority.Input);
    }
}
