using System.Diagnostics;
using DigitalBrain.UI.Models;
using DigitalBrain.UI.UseCases.Ports;

namespace DigitalBrain.UI.Services.Storage;

/// <summary>
/// Pasa los datos de la organización anterior (<c>datos/&lt;movimiento&gt;/&lt;sujeto&gt;/*.txt</c> y
/// <c>artifacts/models/*.pkl</c>) a la de <see cref="DataLayout"/>. Solo mueve: nunca sobrescribe ni borra un archivo,
/// y lo que no puede mover lo deja donde estaba. Al estar todo en su sitio no hace nada.
/// </summary>
public static class LegacyDataMigration
{
    /// <summary>Sujeto que recibe las capturas que no tenían sujeto.</summary>
    public const string OriginalDataSubject = "Datos originales";

    public sealed record Result(int MovedRecordings, int MovedModels, int Skipped);

    public static Result Run(IBackendEnvironment environment)
    {
        int recordings = 0, models = 0, skipped = 0;
        var dataRoot = environment.DataRoot;
        if (Directory.Exists(dataRoot))
        {
            // Cada carpeta que no es de sujeto es un movimiento de la organización anterior.
            foreach (var oldMovement in Directory.GetDirectories(dataRoot).Where(d => !DataLayout.IsSubjectDirectory(d)))
            {
                var folderName = Path.GetFileName(oldMovement);
                var movementFolder = MovementCatalog.BuiltIn
                    .FirstOrDefault(m => string.Equals(m.LegacyFolder, folderName, StringComparison.OrdinalIgnoreCase))?.FolderName ?? folderName;

                MoveFiles(Directory.GetFiles(oldMovement, "*.txt"), Target(dataRoot, OriginalDataSubject, movementFolder), ref recordings, ref skipped);
                foreach (var oldSubject in Directory.GetDirectories(oldMovement))
                {
                    MoveFiles(Directory.GetFiles(oldSubject, "*.txt"), Target(dataRoot, Path.GetFileName(oldSubject), movementFolder),
                        ref recordings, ref skipped);
                    DeleteIfEmpty(oldSubject);
                }
                DeleteIfEmpty(oldMovement);
            }
        }

        // Modelos con el nombre automático «<sujeto>_<algoritmo>» a la carpeta de ese sujeto.
        if (Directory.Exists(environment.LegacyModelsDirectory) && Directory.Exists(dataRoot))
        {
            var subjects = Directory.GetDirectories(dataRoot).Where(DataLayout.IsSubjectDirectory)
                .Select(Path.GetFileName).OfType<string>().OrderByDescending(s => s.Length).ToList();
            foreach (var model in Directory.GetFiles(environment.LegacyModelsDirectory, "*.pkl"))
            {
                var name = Path.GetFileNameWithoutExtension(model);
                var subject = subjects.FirstOrDefault(s => TrainingAlgorithm.All.Any(a =>
                    string.Equals(name, $"{s}_{a.Id}", StringComparison.OrdinalIgnoreCase)));
                if (subject is not null)
                    MoveFiles([model], Path.Combine(dataRoot, subject, DataLayout.ModelsFolder), ref models, ref skipped);
            }
        }

        if (recordings + models + skipped > 0)
            Debug.WriteLine($"Migración de datos: {recordings} grabaciones y {models} modelos movidos; {skipped} sin mover.");
        return new Result(recordings, models, skipped);
    }

    private static string Target(string dataRoot, string subject, string movementFolder)
    {
        // La carpeta de modelos hace reconocible al sujeto aunque todavía no tenga ninguno.
        Directory.CreateDirectory(Path.Combine(dataRoot, subject, DataLayout.ModelsFolder));
        return Path.Combine(dataRoot, subject, DataLayout.MovementsFolder, movementFolder);
    }

    private static void MoveFiles(IEnumerable<string> files, string targetDirectory, ref int moved, ref int skipped)
    {
        foreach (var file in files)
        {
            var target = Path.Combine(targetDirectory, Path.GetFileName(file));
            try
            {
                if (File.Exists(target))
                {
                    skipped++;
                    continue;
                }
                Directory.CreateDirectory(targetDirectory);
                File.Move(file, target);
                moved++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped++;
            }
        }
    }

    private static void DeleteIfEmpty(string directory)
    {
        try
        {
            if (!Directory.EnumerateFileSystemEntries(directory).Any())
                Directory.Delete(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Una carpeta vacía que no se puede borrar no estorba.
        }
    }
}
