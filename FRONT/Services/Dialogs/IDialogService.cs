namespace DigitalBrain.UI.Services.Dialogs;

/// <summary>Selección de archivos y carpetas sin que los ViewModels dependan de ventanas WPF.</summary>
public interface IDialogService
{
    string? PickExecutable(string? initialPath);
    string? PickFolder(string? initialPath);
    void OpenFolder(string path);
}
