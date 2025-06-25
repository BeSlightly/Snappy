using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Dalamud.Game.Command;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Style;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.Configuration;
using ECommons.DalamudServices;
using ECommons.ImGuiMethods;
using ECommons.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OtterGui.Classes;
using OtterGui.Filesystem;
using OtterGui.Log;
using Snappy.Core;
using Snappy.Data;
using Snappy.Features.Mcdf;
using Snappy.Features.Pmp;
using Snappy.IPC;
using Snappy.Models;
using Snappy.UI;
using Snappy.Utils;

namespace Snappy
{
    public sealed class Plugin : IDalamudPlugin
    {
        public string Name => "Snappy";
        private const string CommandName = "/snappy";

        public Logger Log { get; }

        public Configuration Configuration { get; init; }
        public WindowSystem WindowSystem = new("Snappy");
        public FileDialogManager FileDialogManager = new FileDialogManager();
        public DalamudUtil DalamudUtil { get; init; }
        public IpcManager IpcManager { get; init; }
        public SnapshotManager SnapshotManager { get; init; }
        public McdfManager McdfManager { get; init; }
        public PmpExportManager PmpManager { get; init; }
        public FileSystem<Snapshot> SnapshotFS { get; init; }
        public string Version =>
            Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";

        internal StyleModel Style { get; init; }

        internal ConfigWindow ConfigWindow { get; init; }
        internal MainWindow MainWindow { get; init; }

        public event Action? SnapshotsUpdated;

        public void InvokeSnapshotsUpdated()
        {
            SnapshotManager.RefreshSnapshotIndex();
            SnapshotsUpdated?.Invoke();
        }

        private enum SnapshotFormat
        {
            Unknown,
            Old,
            NewButUnversioned,
            NewAndVersioned,
        }

        public Plugin(IDalamudPluginInterface pluginInterface)
        {
            ECommonsMain.Init(pluginInterface, this, ECommons.Module.DalamudReflector);

            Log = new Logger();

            EzConfig.Migrate<Configuration>();
            Configuration = EzConfig.Init<Configuration>();

            if (string.IsNullOrEmpty(Configuration.WorkingDirectory))
            {
                Configuration.WorkingDirectory = Svc.PluginInterface.GetPluginConfigDirectory();
                Directory.CreateDirectory(Configuration.WorkingDirectory);
                EzConfig.Save();
                PluginLog.Information(
                    $"Snapshot directory has been defaulted to: {Configuration.WorkingDirectory}"
                );
            }

            this.Style = StyleModel.Deserialize(
                "DS1H4sIAAAAAAAACq1YS3PbNhD+Kx2ePR6AeJG+xXYbH+KOJ3bHbW60REusaFGlKOXhyX/v4rEACEqumlY+ECD32/cuFn7NquyCnpOz7Cm7eM1+zy5yvfnDPL+fZTP4at7MHVntyMi5MGTwBLJn+HqWLZB46Ygbx64C5kQv/nRo8xXQ3AhZZRdCv2jdhxdHxUeqrJO3Ftslb5l5u/Fa2rfEvP0LWBkBPQiSerF1Cg7wApBn2c5wOMv2juNn9/zieH09aP63g+Kqyr1mI91mHdj5mj3UX4bEG+b5yT0fzRPoNeF1s62e2np+EuCxWc+7z5cLr1SuuCBlkTvdqBCEKmaQxCHJeZmXnFKlgMHVsmnnEZ5IyXMiFUfjwt6yCHvDSitx1212m4gHV0QURY4saMEYl6Q4rsRl18/rPuCZQ+rFJxeARwyAJb5fVmD4NBaJEK3eL331UscuAgflOcY0J5zLUioHpHmhCC0lCuSBwU23r3sfF/0N0wKdoxcGFqHezYZmHypJIkgiSCJIalc8NEM7Utb6ErWlwngt9aUoFRWSB3wilRUl5SRwISUFvhJt9lvDrMgLIjgLzK66tq0228j0H+R3W693l1UfmUd9kqA79MKn9/2sB9lPI8hbofb073vdh1BbQYRgqKzfGbTfTWVqHmnMOcXUpI6BXhzGJjEQCNULmy4x9GpZz1a3Vb8KqaIDz4RPVGZin6dlZPKDSS29baAyRqYfzVGnr0ekaaowTbEw9MLjLnfD0GGT1unHSSlKr2lRyqLA2qU5ESovi6m+lkvqYiZ1/ygxyqrgjDKF8Yr2lp1pd4R7dokhvOBUQk37TCVKQbX4TMVtyuymruKWJCURVEofClYWbNpWCQfFifDwsWnYyXXS8ZxDOI+H0uLToPzrhKg3VV8N3amt1dP/t5goW/E85pg2pB8N8sd623yr3/dNOPYVstELg9cLA8zFCJKapQpEYkPVi9CMA/L/Uv8hrk1hmg9WKKMQXyIxnGFrm6i06MkhBHlIiQ8rI0xx4k/rsLWBsWpbTmmhqFIypcvUHTRgQ859V/bbKaPf1s/dbBcfD0R6NnCWwg/dS3lB4MfQMSrnCY9EK8qEw9uUl4YdHjRQRVFTuu5mq2a9uOvrfVOH0SDHqtXxMjDfi1RA/fyyGb7G5y5KdJg8EnTXdsOHZl1vQyJJQrlCQTDsEBi80HdhO+VwrEP48hwdTRp202yHbgGzhRfu03/UCA4gjglDd44mUT2D2i4UH9coSy8mfjEYN54NfbcOOIZnn15M7YqAH5rFEmdl3eJ8r0N5E9zH0fz71nQQyN+1/zSP6yR2A/l93dazoY6n5DdyiumWc91Xi+u+2zxU/aI+Jipq2QD5tdrfgO3t2P5jcqz9gLEXAEjgFHzcMJUgr5uXyDQsNSxZtCvX81s3r1qLOw0EztC3ORiEs4vssu9W9fqn2263HqpmncFF016PqklGjh1kjQ2NUyUJH08mcIk9gSrqn+jg0XFoqeqTrmDPwQv+PDEr6wl3oljaxcRSRTCyMc/lJJ/lAcnNhMr3WWZ+ES3exrXE+HJ2yNOrowkb97A2cExdXcrYjaFToVDfGSMqnCaDa0pi/vzNMyLG/wQEyzmzfhx7KAwJUn93Fz6v5shD8B+DRAG4Oh+QHYapovAd3/OEQzuiDSdE4c8wjJHh7iiBFFozvP3+NxT8RWGlEQAA"
            )!;

            this.DalamudUtil = new DalamudUtil();
            this.IpcManager = new IpcManager(pluginInterface, this.DalamudUtil);
            this.SnapshotManager = new SnapshotManager(this);
            this.McdfManager = new McdfManager(this, this.SnapshotManager);
            this.PmpManager = new PmpExportManager(this);
            this.SnapshotFS = new FileSystem<Snapshot>();

            SnapshotManager.RefreshSnapshotIndex();
            RunInitialSnapshotMigration();

            ConfigWindow = new ConfigWindow(this);
            MainWindow = new MainWindow(this);
            WindowSystem.AddWindow(ConfigWindow);
            WindowSystem.AddWindow(MainWindow);

            Svc.Commands.AddHandler(
                CommandName,
                new CommandInfo(OnCommand) { HelpMessage = "Opens main Snappy interface" }
            );

            Svc.PluginInterface.UiBuilder.Draw += DrawUI;
            Svc.PluginInterface.UiBuilder.OpenConfigUi += DrawConfigUI;
            Svc.PluginInterface.UiBuilder.DisableGposeUiHide = true;
            Svc.PluginInterface.UiBuilder.OpenMainUi += ToggleMainUI;
        }

        public void Dispose()
        {
            this.WindowSystem.RemoveAllWindows();
            Svc.Commands.RemoveHandler(CommandName);
            this.MainWindow.Dispose();
            this.SnapshotManager.Dispose();
            this.IpcManager.Dispose();
            this.DalamudUtil.Dispose();
            Svc.PluginInterface.UiBuilder.Draw -= DrawUI;
            Svc.PluginInterface.UiBuilder.OpenConfigUi -= DrawConfigUI;
            Svc.PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUI;
            ECommonsMain.Dispose();
        }

        private void OnCommand(string command, string args)
        {
            args = args.Trim().ToLowerInvariant();

            if (args == "config")
            {
                ConfigWindow.IsOpen = true;
                return;
            }

            ToggleMainUI();
        }

        private void DrawUI()
        {
            this.WindowSystem.Draw();
            this.FileDialogManager.Draw();
        }

        public void DrawConfigUI()
        {
            ConfigWindow.Toggle();
        }

        public void ToggleMainUI() => MainWindow.Toggle();

        private async Task<SnapshotFormat> DetectSnapshotFormat(string snapshotJsonPath)
        {
            try
            {
                var jsonContent = await File.ReadAllTextAsync(snapshotJsonPath);
                var jObject = JObject.Parse(jsonContent);

                if (jObject.ContainsKey("FormatVersion"))
                {
                    return SnapshotFormat.NewAndVersioned;
                }

                if (
                    jObject.TryGetValue("FileReplacements", out var fileReplacementsToken)
                    && fileReplacementsToken is JObject fileReplacements
                )
                {
                    var firstEntry = fileReplacements.Properties().FirstOrDefault();
                    if (firstEntry != null)
                    {
                        if (firstEntry.Value.Type == JTokenType.String)
                        {
                            return SnapshotFormat.NewButUnversioned;
                        }
                        if (firstEntry.Value.Type == JTokenType.Array)
                        {
                            return SnapshotFormat.Old;
                        }
                    }
                    else
                    {
                        return SnapshotFormat.NewButUnversioned;
                    }
                }

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

        public void ManuallyRunMigration()
        {
            Task.Run(async () =>
            {
                if (
                    string.IsNullOrEmpty(Configuration.WorkingDirectory)
                    || !Directory.Exists(Configuration.WorkingDirectory)
                )
                {
                    Notify.Warning(
                        "Working directory is not set or does not exist. Cannot run migration."
                    );
                    return;
                }

                var (toMigrate, toUpdate) = await FindOutdatedSnapshotsAsync();

                if (toMigrate.Count == 0 && toUpdate.Count == 0)
                {
                    Notify.Info("No old snapshots found to migrate or update.");
                    return;
                }

                var updatedCount = await UpdateUnversionedSnapshotsAsync(toUpdate);
                var (migrationSuccess, migratedCount) = await BackupAndMigrateOldSnapshotsAsync(
                    toMigrate,
                    isManual: true
                );

                // Final summary notification
                var summary = new List<string>();
                if (updatedCount > 0)
                    summary.Add($"Updated {updatedCount} snapshot(s)");
                if (migratedCount > 0)
                    summary.Add($"Migrated {migratedCount} snapshot(s)");

                if (summary.Any())
                {
                    Notify.Success(string.Join(" and ", summary) + ".");
                }
                else if (toMigrate.Any() && !migrationSuccess)
                {
                    // This case is when migration was supposed to happen but failed at backup. Error is already shown.
                }
                InvokeSnapshotsUpdated();
            });
        }

        private void RunInitialSnapshotMigration()
        {
            Task.Run(async () =>
            {
                if (
                    string.IsNullOrEmpty(Configuration.WorkingDirectory)
                    || !Directory.Exists(Configuration.WorkingDirectory)
                )
                    return;

                var (toMigrate, toUpdate) = await FindOutdatedSnapshotsAsync();
                await UpdateUnversionedSnapshotsAsync(toUpdate);
                var (success, _) = await BackupAndMigrateOldSnapshotsAsync(
                    toMigrate,
                    isManual: false
                );
                if (success)
                {
                    InvokeSnapshotsUpdated();
                }
            });
        }

        private async Task<(
            List<string> toMigrate,
            List<string> toUpdate
        )> FindOutdatedSnapshotsAsync()
        {
            if (
                string.IsNullOrEmpty(Configuration.WorkingDirectory)
                || !Directory.Exists(Configuration.WorkingDirectory)
            )
                return (new List<string>(), new List<string>());

            var dirsToMigrate = new List<string>();
            var dirsToUpdate = new List<string>();
            var allSnapshotDirs = Directory.GetDirectories(Configuration.WorkingDirectory);

            foreach (var dir in allSnapshotDirs)
            {
                var snapshotJsonPath = Path.Combine(dir, "snapshot.json");
                var migratedMarkerPath = Path.Combine(dir, ".migrated");
                if (File.Exists(migratedMarkerPath) || !File.Exists(snapshotJsonPath))
                    continue;
                var format = await DetectSnapshotFormat(snapshotJsonPath);
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

        private async Task<int> UpdateUnversionedSnapshotsAsync(List<string> dirsToUpdate)
        {
            if (dirsToUpdate.Any())
            {
                int updatedCount = 0;
                PluginLog.Information(
                    $"Found {dirsToUpdate.Count} unversioned new-format snapshots. Updating them..."
                );
                foreach (var dir in dirsToUpdate)
                {
                    try
                    {
                        var snapshotJsonPath = Path.Combine(dir, "snapshot.json");
                        var snapshotInfo = JsonConvert.DeserializeObject<SnapshotInfo>(
                            await File.ReadAllTextAsync(snapshotJsonPath)
                        );
                        snapshotInfo!.FormatVersion = 1;
                        await File.WriteAllTextAsync(
                            snapshotJsonPath,
                            JsonConvert.SerializeObject(snapshotInfo, Formatting.Indented)
                        );
                        updatedCount++;
                        PluginLog.Debug(
                            $"Updated {Path.GetFileName(dir)} to include format version."
                        );
                    }
                    catch (Exception e)
                    {
                        PluginLog.Error(
                            $"Failed to update snapshot {Path.GetFileName(dir)}: {e.Message}"
                        );
                    }
                }
                PluginLog.Information("Snapshot update pass complete.");
                return updatedCount;
            }
            return 0;
        }

        private async Task<(bool success, int migratedCount)> BackupAndMigrateOldSnapshotsAsync(
            List<string> dirsToMigrate,
            bool isManual
        )
        {
            if (dirsToMigrate.Any())
            {
                if (isManual)
                {
                    Notify.Info(
                        $"Found {dirsToMigrate.Count} old snapshots to migrate. A backup will be created first."
                    );
                }
                else
                {
                    Notify.Info(
                        $"Old snapshots detected. Starting migration for {dirsToMigrate.Count} directorie(s)..."
                    );
                }

                var backupFileName = $"Snappy_Backup_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.zip";
                var finalBackupPath = Path.Combine(Configuration.WorkingDirectory, backupFileName);

                try
                {
                    var tempZipPath = Path.Combine(Path.GetTempPath(), backupFileName);
                    if (File.Exists(tempZipPath))
                        File.Delete(tempZipPath);
                    using var archive = ZipFile.Open(tempZipPath, ZipArchiveMode.Create);
                    foreach (var dirPath in dirsToMigrate)
                    {
                        var dirInfo = new DirectoryInfo(dirPath);
                        foreach (
                            var file in dirInfo.EnumerateFiles("*", SearchOption.AllDirectories)
                        )
                        {
                            var entryName = Path.GetRelativePath(
                                dirInfo.Parent!.FullName,
                                file.FullName
                            );
                            archive.CreateEntryFromFile(
                                file.FullName,
                                entryName,
                                CompressionLevel.Fastest
                            );
                        }
                    }
                    archive.Dispose();
                    File.Move(tempZipPath, finalBackupPath, true);
                    Notify.Success(
                        $"Successfully created backup of {dirsToMigrate.Count} directories."
                    );
                }
                catch (Exception ex)
                {
                    var errorMsg =
                        "Failed to create snapshot backup. Aborting migration to ensure data safety.";
                    Notify.Error($"{errorMsg}\n{ex.Message}");
                    PluginLog.Error($"{errorMsg} {ex}");
                    return (false, 0);
                }

                int migratedCount = 0;
                foreach (var dir in dirsToMigrate)
                {
                    SnapshotMigrator.Migrate(dir, this.IpcManager);
                    migratedCount++;
                }

                if (migratedCount > 0 && !isManual)
                {
                    Notify.Success("Snapshot migration complete.");
                }
                return (true, migratedCount);
            }
            return (true, 0);
        }
    }
}
