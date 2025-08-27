using Dalamud.Game.Command;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;
using MareSynchronos.Export;
using Snappy.Bypass;
using Snappy.Managers;
using Snappy.PMP;
using Snappy.Utils;
using Snappy.Windows;
using System;
using System.Reflection;
using System.Threading;
using System.Linq;
using System.Collections.Generic;


namespace Snappy
{
    public sealed class Plugin : IDalamudPlugin
    {
        public string Name => "Snappy";
        private const string CommandName = "/snappy";
        public Configuration Configuration { get; init; }
        public IObjectTable Objects { get; init; }
        public WindowSystem WindowSystem = new("Snappy");
        public FileDialogManager FileDialogManager = new FileDialogManager();
        public DalamudUtil DalamudUtil { get; init; }
        public IpcManager IpcManager { get; init; }
        public SnapshotManager SnapshotManager { get; init; }
        public MareCharaFileManager MCDFManager { get; init; }
        public PMPExportManager PMPExportManager { get; init; }
        public string Version =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";

        private ConfigWindow ConfigWindow { get; init; }
        private MainWindow MainWindow { get; init; }
        private System.Threading.Timer? _bypassTimer;


        [PluginService] public static IPluginLog Log { get; private set; } = null!;
        [PluginService] public static ICommandManager CommandManager { get; private set; } = null!;
        [PluginService] public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
        [PluginService] public static IDataManager DataManager { get; private set; } = null!;
        [PluginService] public static IGameInteropProvider GameInteropProvider { get; private set; } = null!;

        public Plugin(
            IFramework framework,
            IObjectTable objectTable,
            IClientState clientState,
            ICondition condition,
            IChatGui chatGui,
            IGameInteropProvider gameInteropProvider)
        {
            ECommonsMain.Init(PluginInterface, this, ECommons.Module.DalamudReflector);

            // Schedule delayed bypass attempt after plugin finishes loading
            ScheduleDelayedBypass();

            this.Objects = objectTable;

            Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            Configuration.Initialize(PluginInterface);

            this.DalamudUtil = new DalamudUtil(clientState, objectTable, framework, condition, chatGui);
            this.IpcManager = new IpcManager(PluginInterface, this.DalamudUtil);

            this.SnapshotManager = new SnapshotManager(this, gameInteropProvider);
            this.MCDFManager = new MareCharaFileManager(this);
            this.PMPExportManager = new PMPExportManager(this);


            ConfigWindow = new ConfigWindow(this);
            MainWindow = new MainWindow(this);
            WindowSystem.AddWindow(ConfigWindow);
            WindowSystem.AddWindow(MainWindow);

            CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Opens main Snappy interface"
            });

            PluginInterface.UiBuilder.Draw += DrawUI;
            PluginInterface.UiBuilder.OpenConfigUi += DrawConfigUI;
            PluginInterface.UiBuilder.DisableGposeUiHide = true;
            PluginInterface.UiBuilder.OpenMainUi += ToggleMainUI;
            
            // Subscribe to plugin changes to reapply bypass when Snowcloak reloads
            PluginInterface.ActivePluginsChanged += OnActivePluginsChanged;
        }

        private void ScheduleDelayedBypass()
        {
            Logger.Info("[Plugin] Scheduling NoSnapService bypass attempt in 5 seconds...");
            _bypassTimer = new System.Threading.Timer(DelayedBypassCallback, null, 5000, Timeout.Infinite);
        }

        private void DelayedBypassCallback(object? state)
        {
            try
            {
                if (NoSnapBypass.TryBypassNoSnapService())
                {
                    Logger.Info("[Plugin] Successfully bypassed Snowcloak's NoSnapService");
                }
                else
                {
                    Logger.Warn("[Plugin] Failed to bypass Snowcloak's NoSnapService - Mare sync may be blocked");
                }
            }
            catch (System.Exception ex)
            {
                Logger.Error("[Plugin] Error during NoSnapService bypass attempt", ex);
            }
            finally
            {
                _bypassTimer?.Dispose();
                _bypassTimer = null;
            }
        }

        public void Dispose()
        {
            this.WindowSystem.RemoveAllWindows();
            CommandManager.RemoveHandler(CommandName);
            this.SnapshotManager.Dispose();
            this.IpcManager.Dispose();
            
            // Cleanup timer
            _bypassTimer?.Dispose();
            
            // Unsubscribe from plugin changes
            PluginInterface.ActivePluginsChanged -= OnActivePluginsChanged;
            
            // Clean up the bypass
            try
            {
                NoSnapBypass.DisableBypass();
            }
            catch (System.Exception ex)
            {
                Logger.Error("[Plugin] Error during NoSnapService bypass cleanup", ex);
            }
            
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
            ConfigWindow.IsOpen = true;
        }

        public void ToggleMainUI() => MainWindow.Toggle();
        
        /// <summary>
        /// Handles plugin loading/unloading events to reapply bypass when Snowcloak reloads
        /// </summary>
        private void OnActivePluginsChanged(Dalamud.Plugin.IActivePluginsChangedEventArgs args)
        {
            try
            {
                // Check if Snowcloak was loaded
                if (args.Kind == Dalamud.Plugin.PluginListInvalidationKind.Loaded && 
                    args.AffectedInternalNames.Contains("Snowcloak"))
                {
                    Logger.Info("[Plugin] Snowcloak was loaded, scheduling bypass reapplication in 5 seconds...");
                    
                    // Schedule bypass reapplication after 5 seconds
                    _bypassTimer?.Dispose();
                    _bypassTimer = new System.Threading.Timer(DelayedBypassCallback, null, 5000, Timeout.Infinite);
                }
            }
            catch (System.Exception ex)
            {
                Logger.Error("[Plugin] Error handling plugin change event", ex);
            }
        }
    }
}