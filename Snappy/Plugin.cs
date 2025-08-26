using Dalamud.Game.Command;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;
using MareSynchronos.Export;
using Snappy.Managers;
using Snappy.PMP;
using Snappy.Utils;
using Snappy.Windows;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;


namespace Snappy
{
    public sealed class Plugin : IDalamudPlugin
    {
        public string Name => "Snappy";
        private readonly string _randomizedInternalName = GenerateRandomInternalName();
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


        [PluginService] public static IPluginLog Log { get; private set; } = null!;
        [PluginService] public static ICommandManager CommandManager { get; private set; } = null!;
        [PluginService] public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
        [PluginService] public static IDataManager DataManager { get; private set; } = null!;
        [PluginService] public static IGameInteropProvider GameInteropProvider { get; private set; } = null!;

        private static string GenerateRandomInternalName()
        {
            var random = new Random();
            return random.Next(100000, 999999).ToString();
        }

        private void RandomizeInternalName()
        {
            try
            {
                // Get the DalamudPluginInterface type
                var pluginInterfaceType = PluginInterface.GetType();
                
                // Get the plugin field (private field that holds the LocalPlugin instance)
                var pluginField = pluginInterfaceType.GetField("plugin", BindingFlags.NonPublic | BindingFlags.Instance);
                if (pluginField != null)
                {
                    var localPlugin = pluginField.GetValue(PluginInterface);
                    if (localPlugin != null)
                    {
                        // Get the manifest field from LocalPlugin
                        var manifestField = localPlugin.GetType().GetField("manifest", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (manifestField != null)
                        {
                            var manifest = manifestField.GetValue(localPlugin);
                            if (manifest != null)
                            {
                                // Set the InternalName property on the manifest
                                var internalNameProperty = manifest.GetType().GetProperty("InternalName");
                                if (internalNameProperty != null && internalNameProperty.CanWrite)
                                {
                                    internalNameProperty.SetValue(manifest, _randomizedInternalName);
                                    Log.Information($"Successfully randomized plugin InternalName to: {_randomizedInternalName}");
                                }
                            }
                        }
                    }
                }
                
                // Override config directory methods to always use "Snappy"
                OverrideConfigDirectory();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to randomize plugin InternalName");
            }
        }
        
        private Configuration LoadSnappyConfig()
        {
            try
            {
                // Get the Snappy config directory directly
                var configDir = GetSnappyConfigDirectory();
                if (!string.IsNullOrEmpty(configDir))
                {
                    var configPath = Path.Combine(configDir, "Snappy.json");
                    
                    if (File.Exists(configPath))
                    {
                        var configJson = File.ReadAllText(configPath);
                        var config = JsonConvert.DeserializeObject<Configuration>(configJson);
                        if (config != null)
                        {
                            config.Initialize(PluginInterface);
                            Log.Information($"Loaded configuration from: {configPath}");
                            return config;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load Snappy config, creating new one");
            }
            
            // Create new config if loading failed
            var newConfig = new Configuration();
            newConfig.Initialize(PluginInterface);
            return newConfig;
        }
        
        private string GetSnappyConfigDirectory()
        {
            try
            {
                var pluginInterfaceType = PluginInterface.GetType();
                var configsField = pluginInterfaceType.GetField("configs", BindingFlags.NonPublic | BindingFlags.Instance);
                
                if (configsField?.GetValue(PluginInterface) is { } configs)
                {
                    var configsType = configs.GetType();
                    var getDirectoryMethod = configsType.GetMethod("GetDirectory");
                    
                    if (getDirectoryMethod != null)
                    {
                        return getDirectoryMethod.Invoke(configs, new object[] { "Snappy" }) as string ?? string.Empty;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to get Snappy config directory");
            }
            
            return string.Empty;
        }
        
        private void OverrideConfigDirectory()
        {
            // Configuration is now handled directly through file operations
            // bypassing the PluginInterface's InternalName-based system
            Log.Information($"Using direct file operations for 'Snappy' config (bypassing {_randomizedInternalName})");
        }

        public Plugin(
            IFramework framework,
            IObjectTable objectTable,
            IClientState clientState,
            ICondition condition,
            IChatGui chatGui,
            IGameInteropProvider gameInteropProvider)
        {
            ECommonsMain.Init(PluginInterface, this, ECommons.Module.DalamudReflector);
            
            // Randomize the plugin's internal name immediately after initialization
            RandomizeInternalName();

            this.Objects = objectTable;

            // Load configuration using hardcoded "Snappy" name to maintain consistent config file
            Configuration = LoadSnappyConfig() ?? new Configuration();
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
        }

        public void Dispose()
        {
            this.WindowSystem.RemoveAllWindows();
            CommandManager.RemoveHandler(CommandName);
            this.SnapshotManager.Dispose();
            this.IpcManager.Dispose();
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
    }
}