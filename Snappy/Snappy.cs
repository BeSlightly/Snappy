using System.Collections.Concurrent;
using System.Reflection;
using Dalamud.Game.Command;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using ECommons;
using ECommons.Configuration;
using OtterGui.Filesystem;
using OtterGui.Log;
using Snappy.Features.Mcdf;
using Snappy.Features.Pcp;
using Snappy.Features.Pmp;
using Snappy.Features.Pmp.ChangedItems;
using Snappy.Services;
using Snappy.Services.SnapshotManager;
using Snappy.UI.Windows;
using Module = ECommons.Module;

namespace Snappy;

public sealed partial class Snappy : IDalamudPlugin
{
    private const string CommandName = "/snappy";
    private readonly ConcurrentQueue<Action> _mainThreadActions = new();

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
        ActorService = new ActorService(IpcManager, Configuration);

        SnapshotIndexService = new SnapshotIndexService(Configuration);
        ActiveSnapshotManager = new ActiveSnapshotManager(IpcManager, Configuration);
        GPoseService = new GPoseService(ActiveSnapshotManager);
        SnapshotFileService = new SnapshotFileService(Configuration, IpcManager, SnapshotIndexService);
        SnapshotApplicationService = new SnapshotApplicationService(IpcManager, ActiveSnapshotManager);

        McdfManager = new McdfManager(Configuration, SnapshotFileService, InvokeSnapshotsUpdated);
        PcpManager = new PcpManager(Configuration, SnapshotFileService, InvokeSnapshotsUpdated);
        PmpManager = new PmpExportManager(Configuration);
        SnapshotChangedItemService = new SnapshotChangedItemService(Log);
        SnapshotFS = new FileSystem<Snapshot>();

        SnapshotIndexService.RefreshSnapshotIndex();
        RunInitialSnapshotMigration();

        ConfigWindow = new ConfigWindow(this, Configuration, ActiveSnapshotManager, IpcManager);
        MainWindow = new MainWindow(this, ActorService, ActiveSnapshotManager, McdfManager, PcpManager, PmpManager,
            SnapshotChangedItemService, SnapshotApplicationService, SnapshotFileService, SnapshotIndexService,
            IpcManager);
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
        Svc.ClientState.Logout += OnLogout;
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
    public ISnapshotChangedItemService SnapshotChangedItemService { get; }
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
        (SnapshotChangedItemService as IDisposable)?.Dispose();
        IpcManager.Dispose(); // Dispose IpcManager to clean up plugin tracking
        Svc.ClientState.Logout -= OnLogout;
        Svc.PluginInterface.UiBuilder.Draw -= DrawUI;
        Svc.PluginInterface.UiBuilder.OpenConfigUi -= DrawConfigUI;
        Svc.PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUI;
        ECommonsMain.Dispose();
    }

    private void OnLogout(int type, int code)
    {
        if (ActiveSnapshotManager.HasActiveSnapshots)
            ActiveSnapshotManager.RevertAllSnapshots();
        MainWindow.ClearActorSelection();
    }
}
