using System.Diagnostics;
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

    private static string ExistingDirectory(string? path) =>
        !string.IsNullOrWhiteSpace(path) && Directory.Exists(path) ? path : "";
}
