using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;

namespace DigitalBrain.UI.Services.Dialogs;

public sealed class WpfDialogService : IDialogService
{
    public string? PickExecutable(string? initialPath)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecciona ConDumpAuto.exe",
            Filter = "Ejecutables (*.exe)|*.exe",
            InitialDirectory = ExistingDirectory(Path.GetDirectoryName(initialPath ?? "")),
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickModel(string? initialPath)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecciona el modelo",
            Filter = "Modelos (*.pkl)|*.pkl",
            InitialDirectory = ExistingDirectory(Path.GetDirectoryName(initialPath ?? "")),
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickRecording(string? initialPath)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecciona una grabación",
            Filter = "Capturas (*.txt)|*.txt",
            InitialDirectory = ExistingDirectory(Path.GetDirectoryName(initialPath ?? "")),
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickFolder(string? initialPath)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Carpeta de salida",
            InitialDirectory = ExistingDirectory(initialPath),
        };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public void OpenFolder(string path)
    {
        if (Directory.Exists(path))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    public bool Confirm(string title, string message) =>
        MessageBox.Show(Application.Current?.MainWindow!, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
            == MessageBoxResult.Yes;

    private static string ExistingDirectory(string? path) =>
        !string.IsNullOrWhiteSpace(path) && Directory.Exists(path) ? path : "";
}
