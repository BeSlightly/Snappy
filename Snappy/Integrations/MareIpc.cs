using System.Collections;
using System.Reflection;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using ECommons.EzIpcManager;
using ECommons.Reflection;
using Snappy.Services;

namespace Snappy.Integrations;

public sealed class MareIpc : IpcSubscriber
{
    private const string PluginName = "LightlessSync";

    private readonly IDalamudPluginInterface _pluginInterface;

    private bool _isInitialized;
    private bool _isUiOpen;

    // Multi-Mare support
    private readonly Dictionary<string, MarePluginInfo> _marePlugins = new()
    {
        { "LightlessSync", new MarePluginInfo("LightlessSync", "LightlessSync") },
        { "Snowcloak", new MarePluginInfo("Snowcloak", "MareSynchronos") }
    };

    private class MarePluginInfo
    {
        public string PluginName { get; }
        public string NamespacePrefix { get; }
        public bool IsAvailable { get; set; }
        public object? Plugin { get; set; }
        public object? PairManager { get; set; }
        public object? FileCacheManager { get; set; }
        public MethodInfo? GetFileCacheByHashMethod { get; set; }

        public MarePluginInfo(string pluginName, string namespacePrefix)
        {
            PluginName = pluginName;
            NamespacePrefix = namespacePrefix;
        }
    }

    public MareIpc() : base(PluginName)
    {
        _pluginInterface = Svc.PluginInterface;

        // Initialize manual IPC subscribers for other Mare plugins
        _lightlessSyncHandledAddresses =
            Svc.PluginInterface.GetIpcSubscriber<List<nint>>("LightlessSync.GetHandledAddresses");
        _snowcloakHandledAddresses =
            Svc.PluginInterface.GetIpcSubscriber<List<nint>>("MareSynchronos.GetHandledAddresses");
    }

    [EzIPC("MareSynchronos.GetHandledAddresses", false, wrapper: SafeWrapper.AnyException)]
    public Func<List<nint>>? GetHandledAddressesIpc { get; set; }

    // Manual IPC subscribers for different plugins
    private ICallGateSubscriber<List<nint>>? _lightlessSyncHandledAddresses;
    private ICallGateSubscriber<List<nint>>? _snowcloakHandledAddresses;

    public override bool IsReady()
    {
        // Check if any Mare plugin is available
        foreach (var pluginName in _marePlugins.Keys)
            if (DalamudReflector.TryGetDalamudPlugin(pluginName, out _, false, true))
                return true;

        return false;
    }

    public Dictionary<string, bool> GetMarePluginStatus()
    {
        // Update availability status for each plugin
        foreach (var kvp in _marePlugins)
            kvp.Value.IsAvailable = DalamudReflector.TryGetDalamudPlugin(kvp.Key, out _, false, true);

        return _marePlugins.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.IsAvailable);
    }

    public void SetUiOpen(bool isOpen)
    {
        if (_isUiOpen == isOpen) return;
        _isUiOpen = isOpen;

        PluginLog.Debug($"UI {(isOpen ? "opened" : "closed")} - using real-time Mare data.");
    }


    public List<ICharacter> GetPairedPlayers()
    {
        if (!_isUiOpen || !IsReady()) return new List<ICharacter>();

        // Get fresh addresses from Mare plugins (Peepy's real approach)
        var pairedAddresses = GetCurrentPairedAddresses();

        // Convert to ICharacter objects
        var result = pairedAddresses
            .Select(addr => Svc.Objects.FirstOrDefault(obj => obj.Address == addr))
            .OfType<ICharacter>()
            .Where(c => c.IsValid())
            .ToList();

        return result;
    }

    /// <summary>
    /// Real-time address checking (Peepy's actual approach - direct IPC call)
    /// </summary>
    public bool IsHandledAddress(nint address)
    {
        // Get fresh data every time (like Peepy does)
        var pairedAddresses = GetCurrentPairedAddresses();
        return pairedAddresses.Contains(address);
    }

    /// <summary>
    /// Get current paired addresses from all Mare plugins (Peepy's real approach - no caching)
    /// </summary>
    private HashSet<nint> GetCurrentPairedAddresses()
    {
        var pairedAddresses = new HashSet<nint>();

        // Update plugin availability first
        foreach (var kvp in _marePlugins)
            kvp.Value.IsAvailable = DalamudReflector.TryGetDalamudPlugin(kvp.Key, out _, false, true);

        // Get addresses from MareSynchronos/Snowcloak
        if (GetHandledAddressesIpc != null)
            try
            {
                var addresses = GetHandledAddressesIpc.Invoke();
                if (addresses != null)
                    foreach (var addr in addresses)
                        pairedAddresses.Add(addr);
            }
            catch (Exception ex)
            {
                PluginLog.Debug($"Failed to get Mare handled addresses: {ex.Message}");
            }

        // Get addresses from LightlessSync
        if (_lightlessSyncHandledAddresses?.HasFunction == true)
            try
            {
                var addresses = _lightlessSyncHandledAddresses.InvokeFunc();
                foreach (var addr in addresses) pairedAddresses.Add(addr);
            }
            catch (Exception ex)
            {
                PluginLog.Debug($"Failed to get LightlessSync handled addresses: {ex.Message}");
            }

        // Get addresses from Snowcloak (if different from Mare)
        if (_snowcloakHandledAddresses?.HasFunction == true)
            try
            {
                var addresses = _snowcloakHandledAddresses.InvokeFunc();
                foreach (var addr in addresses) pairedAddresses.Add(addr);
            }
            catch (Exception ex)
            {
                PluginLog.Debug($"Failed to get Snowcloak handled addresses: {ex.Message}");
            }

        return pairedAddresses;
    }

    public bool IsAddressHandledByLightless(nint address)
    {
        try
        {
            if (_lightlessSyncHandledAddresses?.HasFunction == true)
            {
                var addresses = _lightlessSyncHandledAddresses.InvokeFunc();
                return addresses.Contains(address);
            }
        }
        catch (Exception ex)
        {
            PluginLog.Debug($"Failed LightlessSync address check: {ex.Message}");
        }

        return false;
    }

    public bool IsAddressHandledBySnowcloak(nint address)
    {
        try
        {
            // Prefer direct EzIPC if available
            if (GetHandledAddressesIpc != null)
            {
                var addresses = GetHandledAddressesIpc.Invoke();
                if (addresses != null && addresses.Contains(address))
                    return true;
            }

            if (_snowcloakHandledAddresses?.HasFunction == true)
            {
                var addresses = _snowcloakHandledAddresses.InvokeFunc();
                return addresses.Contains(address);
            }
        }
        catch (Exception ex)
        {
            PluginLog.Debug($"Failed Snowcloak address check: {ex.Message}");
        }

        return false;
    }


    public object? GetCharacterData(ICharacter character)
    {
        // Update plugin availability first
        foreach (var kvp in _marePlugins)
            kvp.Value.IsAvailable = DalamudReflector.TryGetDalamudPlugin(kvp.Key, out _, false, true);

        var availablePlugins = _marePlugins.Values.Where(p => p.IsAvailable).ToList();
        if (!availablePlugins.Any())
        {
            PluginLog.Debug($"No Mare plugins available when trying to get data for {character.Name.TextValue}");
            return null;
        }

        return Svc.Framework.RunOnFrameworkThread(() =>
        {
            InitializeAllPlugins();

            // Try each available plugin to find character data
            foreach (var marePlugin in availablePlugins)
            {
                var pairObject = GetMarePairFromPlugin(character, marePlugin);
                if (pairObject == null) continue;

                var characterData = pairObject.GetFoP("LastReceivedCharacterData");
                if (characterData != null)
                {
                    PluginLog.Debug(
                        $"Successfully retrieved Mare data for {character.Name.TextValue} from {marePlugin.PluginName}");
                    return characterData;
                }
            }

            PluginLog.Debug($"No Mare pair found for character {character.Name.TextValue} in any available plugin.");
            return null;
        }).Result;
    }

    public string? GetFileCachePath(string hash)
    {
        // Update plugin availability first
        foreach (var kvp in _marePlugins)
            kvp.Value.IsAvailable = DalamudReflector.TryGetDalamudPlugin(kvp.Key, out _, false, true);

        var availablePlugins = _marePlugins.Values.Where(p => p.IsAvailable).ToList();
        if (!availablePlugins.Any()) return null;

        InitializeAllPlugins();

        // Try each available plugin to find the file cache
        foreach (var marePlugin in availablePlugins)
        {
            if (marePlugin.FileCacheManager == null || marePlugin.GetFileCacheByHashMethod == null) continue;

            try
            {
                object? fileCacheEntityObject;
                // Check if the method was found during initialization before using it
                if (marePlugin.GetFileCacheByHashMethod == null) continue;

                var methodParams = marePlugin.GetFileCacheByHashMethod.GetParameters();

                if (methodParams.Length == 2 && methodParams[0].ParameterType == typeof(string) &&
                    methodParams[1].ParameterType == typeof(bool)) // This is Snowcloak's signature
                {
                    var parameters = new object[] { hash, false }; // Provide the default value for 'preferSubst'
                    fileCacheEntityObject =
                        marePlugin.GetFileCacheByHashMethod.Invoke(marePlugin.FileCacheManager, parameters);
                }
                else if (methodParams.Length == 1 &&
                         methodParams[0].ParameterType == typeof(string)) // This is LightlessSync's signature
                {
                    fileCacheEntityObject =
                        marePlugin.GetFileCacheByHashMethod.Invoke(marePlugin.FileCacheManager, new object[] { hash });
                }
                else
                {
                    // Signature doesn't match what we expect, so we can't call it.
                    PluginLog.Warning(
                        $"[Mare IPC] Method GetFileCacheByHash for {marePlugin.PluginName} has an unexpected signature.");
                    continue; // Skip to the next plugin
                }

                var filePath = fileCacheEntityObject?.GetFoP("ResolvedFilepath") as string;
                if (!string.IsNullOrEmpty(filePath))
                {
                    PluginLog.Debug($"Found file cache path from {marePlugin.PluginName}: {filePath}");
                    return filePath;
                }
            }
            catch (Exception e)
            {
                PluginLog.Error(
                    $"An exception occurred while reflecting into {marePlugin.PluginName} for file cache path.\n{e}");
            }
        }

        return null;
    }

    private void InitializeAllPlugins()
    {
        foreach (var kvp in _marePlugins)
        {
            var pluginName = kvp.Key;
            var pluginInfo = kvp.Value;

            if (pluginInfo.Plugin != null) continue; // Already initialized

            if (!DalamudReflector.TryGetDalamudPlugin(pluginName, out var marePlugin, true, true))
            {
                pluginInfo.IsAvailable = false;
                continue;
            }

            try
            {
                var host = marePlugin.GetFoP("_host");
                if (host?.GetFoP("Services") is not IServiceProvider serviceProvider)
                {
                    PluginLog.Warning(
                        $"[Mare IPC] Could not get Services from _host for {pluginName}. Plugin may still be loading.");
                    pluginInfo.IsAvailable = false;
                    continue;
                }

                pluginInfo.Plugin = marePlugin;
                pluginInfo.IsAvailable = true;

                var pairManagerType = marePlugin.GetType().Assembly
                    .GetType($"{pluginInfo.NamespacePrefix}.PlayerData.Pairs.PairManager");
                if (pairManagerType != null)
                {
                    pluginInfo.PairManager = serviceProvider.GetService(pairManagerType);
                    if (pluginInfo.PairManager == null)
                        PluginLog.Warning($"[Mare IPC] Could not get PairManager service for {pluginName}.");
                }

                var fileCacheManagerType = marePlugin.GetType().Assembly
                    .GetType($"{pluginInfo.NamespacePrefix}.FileCache.FileCacheManager");
                if (fileCacheManagerType != null)
                {
                    pluginInfo.FileCacheManager = serviceProvider.GetService(fileCacheManagerType);
                    if (pluginInfo.FileCacheManager != null)
                    {
                        // Try to find the Snowcloak version first (string, bool)
                        pluginInfo.GetFileCacheByHashMethod = fileCacheManagerType.GetMethod("GetFileCacheByHash",
                            new[] { typeof(string), typeof(bool) });

                        // If that fails, fall back to the LightlessSync version (string)
                        if (pluginInfo.GetFileCacheByHashMethod == null)
                            pluginInfo.GetFileCacheByHashMethod =
                                fileCacheManagerType.GetMethod("GetFileCacheByHash", new[] { typeof(string) });

                        if (pluginInfo.GetFileCacheByHashMethod == null)
                            PluginLog.Warning(
                                $"[Mare IPC] Could not find method GetFileCacheByHash in FileCacheManager for {pluginName}.");
                    }
                }

                PluginLog.Information($"[Mare IPC] {pluginName} initialization complete.");
            }
            catch (Exception e)
            {
                PluginLog.Error($"[Mare IPC] An exception occurred during {pluginName} initialization: {e}");
                pluginInfo.IsAvailable = false;
            }
        }

        _isInitialized = _marePlugins.Values.Any(p => p.IsAvailable);
    }

    private IDictionary? GetAllMareClientPairsFromPlugin(MarePluginInfo pluginInfo)
    {
        if (!pluginInfo.IsAvailable || pluginInfo.PairManager == null) return null;

        try
        {
            return pluginInfo.PairManager.GetFoP("_allClientPairs") as IDictionary;
        }
        catch (Exception e)
        {
            PluginLog.Error(
                $"An exception occurred while reflecting into {pluginInfo.PluginName} to get client pairs.\n{e}");
            return null;
        }
    }

    private object? GetMarePairFromPlugin(ICharacter character, MarePluginInfo pluginInfo)
    {
        var allClientPairs = GetAllMareClientPairsFromPlugin(pluginInfo);
        if (allClientPairs == null) return null;

        try
        {
            foreach (var pairObject in allClientPairs.Values)
                if (pairObject.GetFoP("PlayerName") is string pairPlayerName &&
                    string.Equals(pairPlayerName, character.Name.TextValue, StringComparison.Ordinal))
                    return pairObject;
        }
        catch (Exception e)
        {
            PluginLog.Error(
                $"An exception occurred while processing {pluginInfo.PluginName} pairs to find a specific pair.\n{e}");
        }

        return null;
    }

    public override void HandlePluginListChanged(IEnumerable<string> affectedPluginNames)
    {
        // Check if any of the plugins we manage (LightlessSync, Snowcloak) were affected
        if (affectedPluginNames.Intersect(_marePlugins.Keys).Any())
        {
            var isAvailable = IsReady();
            if (isAvailable != _wasAvailable)
            {
                PluginLog.Information(
                    $"[{string.Join("/", _marePlugins.Keys)} IPC] A managed plugin's state changed via plugin list event: {_wasAvailable} -> {isAvailable}");
                OnPluginStateChanged(isAvailable, _wasAvailable);
                _wasAvailable = isAvailable;
            }
        }
    }

    protected override void OnPluginStateChanged(bool isAvailable, bool wasAvailable)
    {
        PluginLog.Information($"[Mare IPC] Plugin state changed: {wasAvailable} -> {isAvailable}. Resetting cache.");

        // Reset all plugin states
        foreach (var pluginInfo in _marePlugins.Values)
        {
            pluginInfo.IsAvailable = false;
            pluginInfo.Plugin = null;
            pluginInfo.PairManager = null;
            pluginInfo.FileCacheManager = null;
            pluginInfo.GetFileCacheByHashMethod = null;
        }

        // Reset initialization state
        _isInitialized = false;

        // Re-initialize EzIPC if plugin became available
        if (isAvailable) EzIPC.Init(this, identifier, SafeWrapper.AnyException);
    }
}