using Snappy.Common;

namespace Snappy.Services;

public static class SnapshotMigrator
{
    public static async Task<bool> MigrateAsync(string snapshotPath)
    {
        var paths = SnapshotPaths.From(snapshotPath);

        if (!File.Exists(paths.SnapshotFile) || File.Exists(paths.MigrationMarker)) return false;

        PluginLog.Information($"Found old format snapshot. Migrating: {Path.GetFileName(snapshotPath)}");

        try
        {
            var oldInfo = await JsonUtil.DeserializeAsync<OldSnapshotInfo>(paths.SnapshotFile);
            if (oldInfo == null)
            {
                PluginLog.Error($"Could not deserialize old snapshot.json for {snapshotPath}. Skipping migration.");
                return false;
            }

            Directory.CreateDirectory(paths.FilesDirectory);

            var fileReplacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var legacySourceFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (sourceFileName, gamePaths) in oldInfo.FileReplacements)
            {
                var sourceFilePath = ResolveLegacyFilePath(snapshotPath, sourceFileName);
                if (IsProtectedMigrationPath(paths, sourceFilePath))
                    throw new InvalidDataException(
                        $"Legacy snapshot payload uses a reserved state path: '{sourceFileName}'.");
                if (!File.Exists(sourceFilePath))
                    throw new FileNotFoundException("A legacy snapshot payload is missing.", sourceFilePath);

                legacySourceFiles.Add(sourceFilePath);

                var fileBytes = await File.ReadAllBytesAsync(sourceFilePath);
                var hash = PluginUtil.GetFileHash(fileBytes);
                var newHashedPath = paths.GetPreferredHashedFilePath(hash, sourceFileName);

                if (!File.Exists(newHashedPath))
                {
                    var temporaryPath = AtomicFileUtil.CreateTemporaryOutputPath(newHashedPath);
                    try
                    {
                        await File.WriteAllBytesAsync(temporaryPath, fileBytes);
                        AtomicFileUtil.Complete(temporaryPath, newHashedPath);
                    }
                    finally
                    {
                        AtomicFileUtil.TryDelete(temporaryPath);
                    }
                }

                foreach (var rawGamePath in gamePaths ?? [])
                {
                    var gamePath = GamePathUtil.Normalize(rawGamePath);
                    if (string.IsNullOrEmpty(gamePath))
                        throw new InvalidDataException($"Legacy snapshot contains an invalid game path: '{rawGamePath}'.");
                    fileReplacements[gamePath] = hash;
                }
            }

            var newInfo = SnapshotImportUtil.BuildSnapshotInfo(
                Path.GetFileName(snapshotPath),
                null,
                oldInfo.ManipulationString,
                fileReplacements);

            var glamourerHistory = new GlamourerHistory();
            glamourerHistory.Entries.Add(GlamourerHistoryEntry.Create(oldInfo.GlamourerString,
                "Migrated from old format", newInfo.CurrentFileMapId, oldInfo.CustomizeData));

            var customizeHistory = new CustomizeHistory();
            if (!string.IsNullOrEmpty(oldInfo.CustomizeData))
            {
                var cplusJson = oldInfo.CustomizeData.Trim().StartsWith("{")
                    ? oldInfo.CustomizeData
                    : Encoding.UTF8.GetString(Convert.FromBase64String(oldInfo.CustomizeData));

                var customizeEntry =
                    CustomizeHistoryEntry.CreateFromBase64(oldInfo.CustomizeData, cplusJson,
                        "Migrated from old format", newInfo.CurrentFileMapId);
                customizeHistory.Entries.Add(customizeEntry);
            }

            var glamourerSaved = JsonUtil.Serialize(glamourerHistory, paths.GlamourerHistoryFile);
            var customizeSaved = JsonUtil.Serialize(customizeHistory, paths.CustomizeHistoryFile);
            var snapshotSaved = glamourerSaved && customizeSaved && JsonUtil.Serialize(newInfo, paths.SnapshotFile);
            if (!snapshotSaved || !glamourerSaved || !customizeSaved)
                throw new IOException("Failed to save all migrated snapshot state files.");

            await File.Create(paths.MigrationMarker).DisposeAsync();

            foreach (var sourceFile in legacySourceFiles)
                try
                {
                    File.Delete(sourceFile);
                }
                catch (Exception cleanupException)
                {
                    PluginLog.Warning($"Could not remove migrated legacy file '{sourceFile}': {cleanupException.Message}");
                }

            PluginLog.Information($"Successfully migrated snapshot: {Path.GetFileName(snapshotPath)}");
            return true;
        }
        catch (Exception ex)
        {
            Notify.Error($"Failed to migrate snapshot at {snapshotPath}.\n{ex.Message}");
            PluginLog.Error($"Failed to migrate snapshot at {snapshotPath}: {ex}");
            return false;
        }
    }

    private static string ResolveLegacyFilePath(string snapshotPath, string sourceFileName)
    {
        if (string.IsNullOrWhiteSpace(sourceFileName))
            throw new InvalidDataException("Legacy snapshot contains an empty payload path.");

        var rootPath = Path.GetFullPath(snapshotPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                       + Path.DirectorySeparatorChar;
        var sourcePath = Path.GetFullPath(Path.Combine(rootPath, sourceFileName));
        if (!sourcePath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Legacy snapshot payload escapes its directory: '{sourceFileName}'.");

        return sourcePath;
    }

    private static bool IsProtectedMigrationPath(SnapshotPaths paths, string sourcePath)
    {
        if (string.Equals(sourcePath, Path.GetFullPath(paths.SnapshotFile), StringComparison.OrdinalIgnoreCase)
            || string.Equals(sourcePath, Path.GetFullPath(paths.GlamourerHistoryFile), StringComparison.OrdinalIgnoreCase)
            || string.Equals(sourcePath, Path.GetFullPath(paths.CustomizeHistoryFile), StringComparison.OrdinalIgnoreCase)
            || string.Equals(sourcePath, Path.GetFullPath(paths.MigrationMarker), StringComparison.OrdinalIgnoreCase))
            return true;

        var filesRoot = Path.GetFullPath(paths.FilesDirectory)
                            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        + Path.DirectorySeparatorChar;
        return sourcePath.StartsWith(filesRoot, StringComparison.OrdinalIgnoreCase);
    }

    private record OldSnapshotInfo
    {
        public string GlamourerString { get; init; } = string.Empty;
        public string CustomizeData { get; init; } = string.Empty;
        public string ManipulationString { get; init; } = string.Empty;
        public Dictionary<string, List<string>> FileReplacements { get; init; } = new();
    }
}
