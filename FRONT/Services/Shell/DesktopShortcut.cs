using System.Diagnostics;

namespace DigitalBrain.UI.Services.Shell;

/// <summary>Mantiene en el escritorio un acceso directo a la aplicación.</summary>
public static class DesktopShortcut
{
    private const string ShortcutName = "DigitalBrain.lnk";
    private const string LauncherName = "Ejecutar-DigitalBrain.bat";
    private const int NormalWindow = 1;
    private const int MinimizedWindow = 7;

    /// <summary>
    /// Crea el acceso directo o lo actualiza si el repositorio cambió de carpeta. Apunta al lanzador, que recompila
    /// si hace falta, para no abrir una versión vieja tras actualizar el código. Es opcional: un fallo no impide usar la aplicación.
    /// </summary>
    public static void Ensure(string backendRoot)
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (Environment.ProcessPath is not { } appPath || string.IsNullOrEmpty(desktop)) return;

            var repositoryRoot = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(backendRoot));
            var launcher = repositoryRoot is null ? null : Path.Combine(repositoryRoot, LauncherName);
            var useLauncher = File.Exists(launcher);
            Save(Path.Combine(desktop, ShortcutName), useLauncher ? launcher! : appPath, appPath, useLauncher ? MinimizedWindow : NormalWindow);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"No se pudo crear el acceso directo: {ex.Message}");
        }
    }

    public static void Save(string shortcutPath, string targetPath, string iconPath, int windowStyle)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
                        ?? throw new InvalidOperationException("WScript.Shell no está disponible.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = targetPath;
        shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath);
        shortcut.IconLocation = $"{iconPath},0";
        shortcut.WindowStyle = windowStyle;
        shortcut.Description = "DigitalBrain";
        shortcut.Save();
    }
}
