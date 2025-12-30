using System.IO.Compression;
using Newtonsoft.Json.Linq;
using Snappy.Common;
using Snappy.Services;
using Snappy.Services.SnapshotManager;

namespace Snappy;

public sealed partial class Snappy
{
    private async Task<SnapshotFormat> DetectSnapshotFormat(string snapshotJsonPath)
    {
        try
        {
            var jsonContent = await File.ReadAllTextAsync(snapshotJsonPath);
            var jObject = JObject.Parse(jsonContent);

            if (jObject.ContainsKey("FormatVersion")) return SnapshotFormat.NewAndVersioned;

            if (
                jObject.TryGetValue("FileReplacements", out var fileReplacementsToken)
                && fileReplacementsToken is JObject fileReplacements
            )
            {
                var firstEntry = fileReplacements.Properties().FirstOrDefault();
                if (firstEntry != null)
                    return firstEntry.Value.Type switch
                    {
                        JTokenType.String => SnapshotFormat.NewButUnversioned,
                        JTokenType.Array => SnapshotFormat.Old,
                        _ => SnapshotFormat.Unknown
                    };
                // Empty FileReplacements is a valid new format
                return SnapshotFormat.NewButUnversioned;
            }

            // If it doesn't have a modern FileReplacements object, it's the old format.
            return SnapshotFormat.Old;
        }
        catch (Exception ex)
        {
            PluginLog.Warning(
                $"Could not determine snapshot format for {snapshotJsonPath}, assuming Unknown. Error: {ex.Message}"
            );
            return SnapshotFormat.Unknown;
        }
    }

    private void RunInitialSnapshotMigration()
    {
        ExecuteBackgroundTask(() => PerformMigrationAsync(false));
    }

    public void ManuallyRunMigration()
    {
        ExecuteBackgroundTask(() => PerformMigrationAsync(true));
    }

    private async Task PerformMigrationAsync(bool isManual)
    {
        if (!Configuration.IsValid())
        {
            if (isManual) Notify.Warning("Working directory is not set or does not exist. Cannot run migration.");
            return;
        }

        var (toMigrate, toUpdate) = await FindOutdatedSnapshotsAsync();

        if (toMigrate.Count == 0 && toUpdate.Count == 0)
        {
            if (isManual) Notify.Info("No old snapshots found to migrate or update.");
            return;
        }

        var updatedCount = await UpdateUnversionedSnapshotsAsync(toUpdate);
        var (migrationSuccess, migratedCount) = await BackupAndMigrateOldSnapshots(toMigrate, isManual);

        var summary = new List<string>();
        if (updatedCount > 0) summary.Add($"Updated {updatedCount} snapshot(s)");
        if (migratedCount > 0) summary.Add($"Migrated {migratedCount} snapshot(s)");

        if (summary.Any()) Notify.Success(string.Join(" and ", summary) + ".");

        if (migrationSuccess) QueueAction(InvokeSnapshotsUpdated);
    }

    private async Task<(List<string> toMigrate, List<string> toUpdate)> FindOutdatedSnapshotsAsync()
    {
        if (!Configuration.IsValid()) return (new List<string>(), new List<string>());

        var dirsToMigrate = new List<string>();
        var dirsToUpdate = new List<string>();
        var allSnapshotDirs = Directory.GetDirectories(Configuration.WorkingDirectory);

        foreach (var dir in allSnapshotDirs)
        {
            var paths = SnapshotPaths.From(dir);
            if (File.Exists(paths.MigrationMarker) || !File.Exists(paths.SnapshotFile))
                continue;

            var format = await DetectSnapshotFormat(paths.SnapshotFile);
            switch (format)
            {
                case SnapshotFormat.Old:
                    dirsToMigrate.Add(dir);
                    break;
                case SnapshotFormat.NewButUnversioned:
                    dirsToUpdate.Add(dir);
                    break;
            }
        }

        return (dirsToMigrate, dirsToUpdate);
    }

    private async Task<int> UpdateUnversionedSnapshotsAsync(IReadOnlyCollection<string> dirsToUpdate)
    {
        if (!dirsToUpdate.Any()) return 0;

        var updatedCount = 0;
        PluginLog.Information($"Found {dirsToUpdate.Count} unversioned new-format snapshots. Updating them...");
        foreach (var dir in dirsToUpdate)
            try
            {
                var paths = SnapshotPaths.From(dir);
                var snapshotInfo = await JsonUtil.DeserializeAsync<SnapshotInfo>(paths.SnapshotFile);
                if (snapshotInfo != null)
                {
                    snapshotInfo.FormatVersion = 1;
                    JsonUtil.Serialize(snapshotInfo, paths.SnapshotFile);
                    updatedCount++;
                    PluginLog.Debug($"Updated {Path.GetFileName(dir)} to include format version.");
                }
            }
            catch (Exception e)
            {
                PluginLog.Error($"Failed to update snapshot {Path.GetFileName(dir)}: {e.Message}");
            }

        PluginLog.Information("Snapshot update pass complete.");
        return updatedCount;
    }

    private async Task<(bool success, int migratedCount)> BackupAndMigrateOldSnapshots(
        IReadOnlyCollection<string> dirsToMigrate, bool isManual)
    {
        if (!dirsToMigrate.Any()) return (true, 0);

        var notification = isManual
            ? $"Found {dirsToMigrate.Count} old snapshots to migrate. A backup will be created first."
            : $"Old snapshots detected. Starting migration for {dirsToMigrate.Count} directorie(s)...";
        Notify.Info(notification);

        var backupSuccess = await CreateMigrationBackup(dirsToMigrate);
        if (!backupSuccess) return (false, 0);

        var migratedCount = 0;
        foreach (var dir in dirsToMigrate)
        {
            await SnapshotMigrator.MigrateAsync(dir, IpcManager);
            migratedCount++;
        }

        if (migratedCount > 0 && !isManual) Notify.Success("Snapshot migration complete.");
        return (true, migratedCount);
    }

    private async Task<bool> CreateMigrationBackup(IReadOnlyCollection<string> dirsToBackup)
    {
        var backupFileName = $"Snappy_Backup_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.zip";
        var finalBackupPath = Path.Combine(Configuration.WorkingDirectory, backupFileName);
        var tempZipPath = Path.Combine(Path.GetTempPath(), backupFileName);

        try
        {
            if (File.Exists(tempZipPath)) File.Delete(tempZipPath);

            await Task.Run(() =>
            {
                using var archive = ZipFile.Open(tempZipPath, ZipArchiveMode.Create);
                foreach (var dirPath in dirsToBackup)
                {
                    var dirInfo = new DirectoryInfo(dirPath);
                    if (!dirInfo.Exists) continue;

                    foreach (var file in dirInfo.EnumerateFiles("*", SearchOption.AllDirectories))
                    {
                        var entryName = Path.GetRelativePath(dirInfo.Parent!.FullName, file.FullName);
                        archive.CreateEntryFromFile(file.FullName, entryName, CompressionLevel.Fastest);
                    }
                }
            });

            File.Move(tempZipPath, finalBackupPath, true);
            Notify.Success($"Successfully created backup of {dirsToBackup.Count} directories.");
            return true;
        }
        catch (Exception ex)
        {
            var errorMsg = "Failed to create snapshot backup. Aborting migration to ensure data safety.";
            Notify.Error($"{errorMsg}\n{ex.Message}");
            PluginLog.Error($"{errorMsg} {ex}");
            return false;
        }
    }

    private enum SnapshotFormat
    {
        Unknown,
        Old,
        NewButUnversioned,
        NewAndVersioned
    }
}
