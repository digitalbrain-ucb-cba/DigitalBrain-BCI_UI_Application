using System.Diagnostics;
using DigitalBrain.UI.Models;
using DigitalBrain.UI.UseCases.Ports;

namespace DigitalBrain.UI.Services.Storage;

/// <summary>
/// Pasa los datos de organizaciones anteriores a la de <see cref="DataLayout"/>. Solo mueve: nunca sobrescribe ni borra un archivo,
/// y lo que no puede mover lo deja donde estaba. Al estar todo en su sitio no hace nada.
/// <list type="number">
/// <item><c>datos/&lt;movimiento&gt;/&lt;sujeto&gt;/*.txt</c> → <c>datos/&lt;sujeto&gt;/movimientos/&lt;movimiento&gt;/</c> (sujeto sin tipo).</item>
/// <item><c>artifacts/models/&lt;sujeto&gt;_&lt;algoritmo&gt;.pkl</c> → <c>datos/&lt;sujeto&gt;/modelos/</c>.</item>
/// <item>Sujetos con prefijo de tipo, <c>datos/ModelMI_Arturo/</c> → <c>datos/Arturo/MI/</c>.</item>
/// </list>
/// Los sujetos sin tipo se quedan como están: no se puede saber si sus grabaciones son de MI o de M.
/// </summary>
public static class LegacyDataMigration
{
    /// <summary>Sujeto que recibe las capturas que no tenían sujeto.</summary>
    public const string OriginalDataSubject = "Datos originales";

    public sealed record Result(int MovedRecordings, int MovedModels, int MovedFiles, int Skipped);

    public static Result Run(IBackendEnvironment environment)
    {
        int recordings = 0, models = 0, files = 0, skipped = 0;
        var dataRoot = environment.DataRoot;
        if (Directory.Exists(dataRoot))
        {
            // Una carpeta con capturas directamente o en sus subcarpetas, y que no es de un sujeto, es un movimiento de la
            // primera organización. Las vacías se ignoran: un sujeto recién añadido aún no tiene nada dentro.
            foreach (var oldMovement in Directory.GetDirectories(dataRoot).Where(IsOldMovementDirectory))
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
            var subjects = Directory.GetDirectories(dataRoot).Where(DataLayout.HasUntypedFolders)
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

        // Sujetos con el tipo como prefijo: su contenido pasa a la carpeta del tipo dentro de la del sujeto sin prefijo.
        if (Directory.Exists(dataRoot))
        {
            foreach (var oldSubject in Directory.GetDirectories(dataRoot))
            {
                var folderName = Path.GetFileName(oldSubject);
                var type = ModelType.All.FirstOrDefault(t => folderName.StartsWith(t.LegacyPrefix, StringComparison.OrdinalIgnoreCase));
                if (type is null) continue;
                var subject = CatalogNames.Normalize(folderName[type.LegacyPrefix.Length..]);
                if (CatalogNames.Validate(subject, "sujeto") is not null) continue;

                MoveTree(oldSubject, Path.Combine(dataRoot, subject, type.Folder), ref files, ref skipped);
                DeleteEmptyTree(oldSubject);
            }
        }

        if (recordings + models + files + skipped > 0)
            Debug.WriteLine($"Migración de datos: {recordings} grabaciones, {models} modelos y {files} archivos de sujetos con prefijo movidos; {skipped} sin mover.");
        return new Result(recordings, models, files, skipped);
    }

    private static bool IsOldMovementDirectory(string directory)
    {
        if (DataLayout.HasTypeFolders(directory) || DataLayout.HasUntypedFolders(directory)) return false;
        try
        {
            return Directory.EnumerateFiles(directory, "*.txt").Any() ||
                   Directory.EnumerateDirectories(directory).Any(sub => Directory.EnumerateFiles(sub, "*.txt").Any());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string Target(string dataRoot, string subject, string movementFolder)
    {
        // La carpeta de modelos hace reconocible al sujeto aunque todavía no tenga ninguno.
        Directory.CreateDirectory(Path.Combine(dataRoot, subject, DataLayout.ModelsFolder));
        return Path.Combine(dataRoot, subject, DataLayout.MovementsFolder, movementFolder);
    }

    /// <summary>Mueve los archivos de <paramref name="source"/> y sus subcarpetas a la misma ruta relativa en <paramref name="target"/>.</summary>
    private static void MoveTree(string source, string target, ref int moved, ref int skipped)
    {
        try
        {
            Directory.CreateDirectory(target);
            MoveFiles(Directory.GetFiles(source), target, ref moved, ref skipped);
            foreach (var directory in Directory.GetDirectories(source))
                MoveTree(directory, Path.Combine(target, Path.GetFileName(directory)), ref moved, ref skipped);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            skipped++;
        }
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

    private static void DeleteEmptyTree(string directory)
    {
        try
        {
            foreach (var sub in Directory.GetDirectories(directory))
                DeleteEmptyTree(sub);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }
        DeleteIfEmpty(directory);
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
