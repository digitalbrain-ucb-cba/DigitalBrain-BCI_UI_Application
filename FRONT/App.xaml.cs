using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using DigitalBrain.UI.Services.Hardware;
using DigitalBrain.UI.Services.Backend;
using DigitalBrain.UI.Services.Navigation;
using DigitalBrain.UI.Services.Signal;
using DigitalBrain.UI.ViewModels;
using DigitalBrain.UI.Views;

namespace DigitalBrain.UI;

public partial class App : Application
{
    private readonly IServiceProvider _serviceProvider;

    public App()
    {
        var services = new ServiceCollection();

        // Servicios base de infraestructura y hardware (reutilizables)
        services.AddSingleton<ICaptureService, ConDumpCaptureService>();
        services.AddSingleton<IBackendBridge, PythonCliBridge>();
        services.AddSingleton<SignalRingBuffer>();

        // Servicio de navegación entre vistas
        services.AddSingleton<Func<Type, BaseViewModel>>(sp => viewModelType => (BaseViewModel)sp.GetRequiredService(viewModelType));
        services.AddSingleton<INavigationService, NavigationService>();

        // ViewModel y Ventana Principal
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }
}
