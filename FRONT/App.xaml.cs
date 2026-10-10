using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using DigitalBrain.UI.Services.Hardware;
using DigitalBrain.UI.Services.Backend;
using DigitalBrain.UI.Services.Dialogs;
using DigitalBrain.UI.Services.Navigation;
using DigitalBrain.UI.Services.Signal;
using DigitalBrain.UI.Services.Storage;
using DigitalBrain.UI.Services.Theme;
using DigitalBrain.UI.UseCases;
using DigitalBrain.UI.UseCases.Ports;
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
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<IBackendEnvironment, BackendEnvironment>();
        services.AddSingleton<ICaptureService, ConDumpCaptureService>();
        services.AddSingleton<IBackendBridge>(sp =>
        {
            var environment = sp.GetRequiredService<IBackendEnvironment>();
            return new PythonCliBridge(environment.PythonExecutable, environment.BackendRoot);
        });
        services.AddSingleton<ICaptureGateway, PythonCliCaptureGateway>();
        services.AddSingleton<ISessionLog, JsonlSessionLog>();
        services.AddSingleton<IRecordingRepository, FileSystemRecordingRepository>();
        services.AddSingleton<SignalRingBuffer>();
        services.AddSingleton<IDialogService, WpfDialogService>();
        services.AddSingleton<IThemeService, ThemeService>();

        // Casos de uso
        services.AddTransient<RecordSessionUseCase>();
        services.AddTransient<TrainModelUseCase>();

        // Servicio de navegación entre vistas
        services.AddSingleton<Func<Type, BaseViewModel>>(sp => viewModelType => (BaseViewModel)sp.GetRequiredService(viewModelType));
        services.AddSingleton<INavigationService, NavigationService>();

        // ViewModels y Ventana Principal
        services.AddSingleton<SystemStatusViewModel>();
        services.AddSingleton<CaptureTrainingViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var themeService = _serviceProvider.GetRequiredService<IThemeService>();
        themeService.Apply(themeService.Current);

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        _serviceProvider.GetRequiredService<INavigationService>().NavigateTo<CaptureTrainingViewModel>();
        mainWindow.Show();
    }
}
