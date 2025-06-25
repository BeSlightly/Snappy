using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ECommons.ImGuiMethods;
using ECommons.Logging;
using Newtonsoft.Json;
using Snappy.IPC;
using Snappy.Models;
using Snappy.Utils;

namespace Snappy.Data
{
    public static class SnapshotMigrator
    {
        private record OldSnapshotInfo
        {
            public string GlamourerString { get; set; } = string.Empty;
            public string CustomizeData { get; set; } = string.Empty;
            public string ManipulationString { get; set; } = string.Empty;
            public Dictionary<string, List<string>> FileReplacements { get; set; } = new();
        }

        public static void Migrate(string snapshotPath, IpcManager ipcManager)
        {
            var oldSnapshotJsonPath = Path.Combine(snapshotPath, "snapshot.json");
            var migrationMarkerPath = Path.Combine(snapshotPath, ".migrated");
            var filesPath = Path.Combine(snapshotPath, "_files");

            if (!File.Exists(oldSnapshotJsonPath) || File.Exists(migrationMarkerPath))
            {
                return;
            }

            PluginLog.Information(
                $"Found old format snapshot. Migrating: {Path.GetFileName(snapshotPath)}"
            );

            try
            {
                var oldInfo = JsonConvert.DeserializeObject<OldSnapshotInfo>(
                    File.ReadAllText(oldSnapshotJsonPath)
                );
                if (oldInfo == null)
                {
                    PluginLog.Error(
                        $"Could not deserialize old snapshot.json for {snapshotPath}. Skipping migration."
                    );
                    return;
                }

                Directory.CreateDirectory(filesPath);

                var newInfo = new SnapshotInfo
                {
                    SourceActor = Path.GetFileName(snapshotPath),
                    LastUpdate = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    ManipulationString = oldInfo.ManipulationString,
                };

                foreach (var replacement in oldInfo.FileReplacements)
                {
                    var sourceFilePath = Path.Combine(snapshotPath, replacement.Key);
                    if (!File.Exists(sourceFilePath))
                    {
                        PluginLog.Warning(
                            $"Missing file during migration: {sourceFilePath}. Skipping."
                        );
                        continue;
                    }

                    var fileBytes = File.ReadAllBytes(sourceFilePath);
                    var hash = Crypto.GetHash(fileBytes);
                    var newHashedPath = Path.Combine(filesPath, $"{hash}.dat");

                    if (!File.Exists(newHashedPath))
                    {
                        File.WriteAllBytes(newHashedPath, fileBytes);
                    }

                    foreach (var gamePath in replacement.Value)
                    {
                        newInfo.FileReplacements[gamePath] = hash;
                    }
                }

                var glamourerHistory = new GlamourerHistory();
                glamourerHistory.Entries.Add(
                    GlamourerHistoryEntry.Create(
                        oldInfo.GlamourerString,
                        "Migrated from old format"
                    )
                );

                var customizeHistory = new CustomizeHistory();
                if (
                    !string.IsNullOrEmpty(oldInfo.CustomizeData)
                    && ipcManager.IsCustomizePlusAvailable()
                )
                {
                    string cplusJson = oldInfo.CustomizeData.Trim().StartsWith("{")
                        ? oldInfo.CustomizeData
                        : Encoding.UTF8.GetString(Convert.FromBase64String(oldInfo.CustomizeData));

                    var customizeEntry = CustomizeHistoryEntry.CreateFromBase64(
                        oldInfo.CustomizeData,
                        cplusJson,
                        "Migrated from old format"
                    );
                    customizeHistory.Entries.Add(customizeEntry);
                }

                foreach (var file in Directory.GetFiles(snapshotPath))
                {
                    File.Delete(file);
                }
                foreach (var dir in Directory.GetDirectories(snapshotPath))
                {
                    if (Path.GetFileName(dir).Equals("_files", StringComparison.OrdinalIgnoreCase))
                        continue;
                    Directory.Delete(dir, true);
                }

                File.WriteAllText(
                    Path.Combine(snapshotPath, "snapshot.json"),
                    JsonConvert.SerializeObject(newInfo, Formatting.Indented)
                );
                File.WriteAllText(
                    Path.Combine(snapshotPath, "glamourer_history.json"),
                    JsonConvert.SerializeObject(glamourerHistory, Formatting.Indented)
                );
                File.WriteAllText(
                    Path.Combine(snapshotPath, "customize_history.json"),
                    JsonConvert.SerializeObject(customizeHistory, Formatting.Indented)
                );

                File.Create(migrationMarkerPath).Close();

                PluginLog.Information(
                    $"Successfully migrated snapshot: {Path.GetFileName(snapshotPath)}"
                );
            }
            catch (Exception ex)
            {
                Notify.Error($"Failed to migrate snapshot at {snapshotPath}.\n{ex.Message}");
                PluginLog.Error($"Failed to migrate snapshot at {snapshotPath}: {ex}");
                try
                {
                    Directory.Move(snapshotPath, snapshotPath + "_migration_failed");
                }
                catch { }
            }
        }
    }
}
