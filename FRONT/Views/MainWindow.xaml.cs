using System.Windows;
using System.Windows.Interop;
using DigitalBrain.UI.Services.Hardware;
using DigitalBrain.UI.ViewModels;

namespace DigitalBrain.UI.Views;

public partial class MainWindow : Window
{
    private const int WmDeviceChange = 0x0219;
    private readonly IHeadsetDetector _detector;

    public MainWindow(MainViewModel viewModel, IHeadsetDetector detector)
    {
        InitializeComponent();
        DataContext = viewModel;
        _detector = detector;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(HwndHandler);
        }
    }

    private IntPtr HwndHandler(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmDeviceChange)
        {
            _detector.Poll();
        }
        return IntPtr.Zero;
    }
}
