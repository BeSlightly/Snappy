using Snappy.Common;

namespace Snappy.Services;

public static class SnapshotMigrator
{
    public static async Task MigrateAsync(string snapshotPath, IIpcManager ipcManager)
    {
        var paths = SnapshotPaths.From(snapshotPath);

        if (!File.Exists(paths.SnapshotFile) || File.Exists(paths.MigrationMarker)) return;

        PluginLog.Information($"Found old format snapshot. Migrating: {Path.GetFileName(snapshotPath)}");

        try
        {
            var oldInfo = await JsonUtil.DeserializeAsync<OldSnapshotInfo>(paths.SnapshotFile);
            if (oldInfo == null)
            {
                PluginLog.Error($"Could not deserialize old snapshot.json for {snapshotPath}. Skipping migration.");
                return;
            }

            Directory.CreateDirectory(paths.FilesDirectory);

            var newInfo = new SnapshotInfo
            {
                SourceActor = Path.GetFileName(snapshotPath),
                LastUpdate = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                ManipulationString = oldInfo.ManipulationString
            };

            foreach (var (sourceFileName, gamePaths) in oldInfo.FileReplacements)
            {
                var sourceFilePath = Path.Combine(snapshotPath, sourceFileName);
                if (!File.Exists(sourceFilePath))
                {
                    PluginLog.Warning($"Missing file during migration: {sourceFilePath}. Skipping.");
                    continue;
                }

                var fileBytes = await File.ReadAllBytesAsync(sourceFilePath);
                var hash = PluginUtil.GetFileHash(fileBytes);
                var newHashedPath = paths.GetHashedFilePath(hash);

                if (!File.Exists(newHashedPath)) await File.WriteAllBytesAsync(newHashedPath, fileBytes);

                foreach (var gamePath in gamePaths) newInfo.FileReplacements[gamePath] = hash;
            }

            var glamourerHistory = new GlamourerHistory();
            glamourerHistory.Entries.Add(GlamourerHistoryEntry.Create(oldInfo.GlamourerString,
                "Migrated from old format"));

            var customizeHistory = new CustomizeHistory();
            if (!string.IsNullOrEmpty(oldInfo.CustomizeData) && ipcManager.IsCustomizePlusAvailable())
            {
                var cplusJson = oldInfo.CustomizeData.Trim().StartsWith("{")
                    ? oldInfo.CustomizeData
                    : Encoding.UTF8.GetString(Convert.FromBase64String(oldInfo.CustomizeData));

                var customizeEntry =
                    CustomizeHistoryEntry.CreateFromBase64(oldInfo.CustomizeData, cplusJson,
                        "Migrated from old format");
                customizeHistory.Entries.Add(customizeEntry);
            }

            foreach (var file in Directory.GetFiles(snapshotPath)) File.Delete(file);

            foreach (var dir in Directory.GetDirectories(snapshotPath))
            {
                if (Path.GetFileName(dir)
                    .Equals(Constants.FilesSubdirectory, StringComparison.OrdinalIgnoreCase)) continue;

                Directory.Delete(dir, true);
            }

            JsonUtil.Serialize(newInfo, paths.SnapshotFile);
            JsonUtil.Serialize(glamourerHistory, paths.GlamourerHistoryFile);
            JsonUtil.Serialize(customizeHistory, paths.CustomizeHistoryFile);

            await File.Create(paths.MigrationMarker).DisposeAsync();

            PluginLog.Information($"Successfully migrated snapshot: {Path.GetFileName(snapshotPath)}");
        }
        catch (Exception ex)
        {
            Notify.Error($"Failed to migrate snapshot at {snapshotPath}.\n{ex.Message}");
            PluginLog.Error($"Failed to migrate snapshot at {snapshotPath}: {ex}");
            try
            {
                Directory.Move(snapshotPath, snapshotPath + "_migration_failed");
            }
            catch
            {
                // ignored
            }
        }
    }

    private record OldSnapshotInfo
    {
        public string GlamourerString { get; init; } = string.Empty;
        public string CustomizeData { get; init; } = string.Empty;
        public string ManipulationString { get; init; } = string.Empty;
        public Dictionary<string, List<string>> FileReplacements { get; init; } = new();
    }
}