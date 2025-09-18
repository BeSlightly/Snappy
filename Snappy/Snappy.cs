using System.Collections.Concurrent;
using System.IO.Compression;
using System.Reflection;
using Dalamud.Game.Command;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using ECommons;
using ECommons.Configuration;
using Newtonsoft.Json.Linq;
using OtterGui.Filesystem;
using OtterGui.Log;
using Snappy.Common;
using Snappy.Features.Mcdf;
using Snappy.Features.Pcp;
using Snappy.Features.Pmp;
using Snappy.Services;
using Snappy.Services.SnapshotManager;
using Snappy.UI.Windows;
using LuminaWorld = Lumina.Excel.Sheets.World;
using Module = ECommons.Module;

namespace Snappy;

public sealed class Snappy : IDalamudPlugin
{
    private const string CommandName = "/snappy";
    private readonly ConcurrentQueue<Action> _mainThreadActions = new();

    // World names dictionary for HomeWorld lookup (similar to WhoList)
    public static Dictionary<uint, string> WorldNames { get; private set; } = new();


    public Snappy(IDalamudPluginInterface pluginInterface)
    {
        ECommonsMain.Init(pluginInterface, this, Module.DalamudReflector);

        Log = new Logger();

        // Initialize WorldNames dictionary (similar to WhoList approach)
        InitializeWorldNames();

        EzConfig.Migrate<Configuration>();
        Configuration = EzConfig.Init<Configuration>();

        if (string.IsNullOrEmpty(Configuration.WorkingDirectory))
        {
            Configuration.WorkingDirectory = Svc.PluginInterface.GetPluginConfigDirectory();
            Directory.CreateDirectory(Configuration.WorkingDirectory);
            Configuration.Save();
            PluginLog.Information(
                $"Snapshot directory has been defaulted to: {Configuration.WorkingDirectory}"
            );
        }

        IpcManager = new IpcManager();
        ActorService = new ActorService(IpcManager);

        SnapshotIndexService = new SnapshotIndexService(Configuration);
        ActiveSnapshotManager = new ActiveSnapshotManager(IpcManager, Configuration);
        GPoseService = new GPoseService(ActiveSnapshotManager);
        SnapshotFileService = new SnapshotFileService(Configuration, IpcManager, SnapshotIndexService);
        SnapshotApplicationService = new SnapshotApplicationService(IpcManager, ActiveSnapshotManager);

        McdfManager = new McdfManager(Configuration, SnapshotFileService, InvokeSnapshotsUpdated);
        PcpManager = new PcpManager(Configuration, SnapshotFileService, InvokeSnapshotsUpdated);
        PmpManager = new PmpExportManager(Configuration);
        SnapshotFS = new FileSystem<Snapshot>();

        SnapshotIndexService.RefreshSnapshotIndex();
        RunInitialSnapshotMigration();

        ConfigWindow = new ConfigWindow(this, Configuration, ActiveSnapshotManager, IpcManager);
        MainWindow = new MainWindow(this, ActorService, ActiveSnapshotManager, McdfManager, PcpManager, PmpManager,
            SnapshotApplicationService,
            SnapshotFileService, SnapshotIndexService, IpcManager);
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

    public string Name => "Snappy";

    public Logger Log { get; }

    public Configuration Configuration { get; }
    public WindowSystem WindowSystem { get; } = new("Snappy");
    public FileDialogManager FileDialogManager { get; } = new();

    public IIpcManager IpcManager { get; }
    public IActorService ActorService { get; }
    public ISnapshotApplicationService SnapshotApplicationService { get; }
    public ISnapshotIndexService SnapshotIndexService { get; }
    public IActiveSnapshotManager ActiveSnapshotManager { get; }
    public IGPoseService GPoseService { get; }
    public ISnapshotFileService SnapshotFileService { get; }
    public IMcdfManager McdfManager { get; }
    public IPcpManager PcpManager { get; }
    public IPmpExportManager PmpManager { get; }
    public FileSystem<Snapshot> SnapshotFS { get; }

    public string Version =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";

    internal ConfigWindow ConfigWindow { get; }
    internal MainWindow MainWindow { get; }

    public void Dispose()
    {
        WindowSystem.RemoveAllWindows();
        Svc.Commands.RemoveHandler(CommandName);
        MainWindow.Dispose();
        GPoseService.Dispose();
        IpcManager.Dispose(); // Dispose IpcManager to clean up plugin tracking
        Svc.PluginInterface.UiBuilder.Draw -= DrawUI;
        Svc.PluginInterface.UiBuilder.OpenConfigUi -= DrawConfigUI;
        Svc.PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUI;
        ECommonsMain.Dispose();
    }

    public event Action? SnapshotsUpdated;

    public void QueueAction(Action action)
    {
        _mainThreadActions.Enqueue(action);
    }

    public void ExecuteBackgroundTask(Func<Task> task)
    {
        Task.Run(async () =>
        {
            try
            {
                await task();
            }
            catch (Exception ex)
            {
                PluginLog.Error($"A background task failed: {ex}");
            }
        });
    }

    public void InvokeSnapshotsUpdated()
    {
        SnapshotIndexService.RefreshSnapshotIndex();
        SnapshotsUpdated?.Invoke();
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
        while (_mainThreadActions.TryDequeue(out var action)) action.Invoke();

        WindowSystem.Draw();
        FileDialogManager.Draw();
    }


    public void DrawConfigUI()
    {
        ConfigWindow.Toggle();
    }


    public void ToggleMainUI()
    {
        MainWindow.Toggle();
    }


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

    private static void InitializeWorldNames()
    {
        try
        {
            // Use the same approach as WhoList to build WorldNames dictionary
            var worldSheet = Svc.Data.GetExcelSheet<LuminaWorld>();
            if (worldSheet != null)
            {
                // Filter to valid, accessible worlds similar to Penumbra DictWorld:
                // - Non-empty name
                // - Has a DataCenter (RowId != 0)
                // - If IsPublic, include
                // - Otherwise, only include if the first character is an uppercase Latin letter
                WorldNames = worldSheet
                    .Where(world =>
                    {
                        var nameStr = world.Name.ToString();
                        if (string.IsNullOrEmpty(nameStr))
                            return false;
                        if (world.DataCenter.RowId == 0)
                            return false;
                        if (world.IsPublic)
                            return true;
                        var first = nameStr[0];
                        return char.IsUpper(first);
                    })
                    .ToDictionary(world => world.RowId, world => world.Name.ToString());

                PluginLog.Debug($"Initialized WorldNames dictionary with {WorldNames.Count} entries.");
            }
            else
            {
                PluginLog.Warning("Failed to load World sheet for WorldNames initialization.");
            }
        }
        catch (Exception ex)
        {
            PluginLog.Error($"Error initializing WorldNames: {ex.Message}");
            WorldNames = new Dictionary<uint, string>();
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