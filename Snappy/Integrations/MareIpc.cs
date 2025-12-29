using System.Collections;
using System.Reflection;
using Dalamud.Plugin.Ipc;
using ECommons.Reflection;
using Snappy.Services;

namespace Snappy.Integrations;

public sealed class MareIpc : IpcSubscriber
{
    private const string PluginName = "LightlessSync";

    private bool _isUiOpen;

    // Multi-Mare support
    private readonly Dictionary<string, MarePluginInfo> _marePlugins = new()
    {
        { "LightlessSync", new MarePluginInfo("LightlessSync", "LightlessSync") },
        { "Snowcloak", new MarePluginInfo("Snowcloak", "Snowcloak") },
        { "MareSempiterne", new MarePluginInfo("Player Sync", "MareSynchronos") }
    };

    private class MarePluginInfo
    {
        public string PluginName { get; }
        public string NamespacePrefix { get; }
        public bool IsAvailable { get; set; }
        public object? Plugin { get; set; }
        public object? PairManager { get; set; }

        public MarePluginInfo(string pluginName, string namespacePrefix)
        {
            PluginName = pluginName;
            NamespacePrefix = namespacePrefix;
        }
    }

    public MareIpc() : base(PluginName)
    {
        // Initialize manual IPC subscribers
        _lightlessSyncHandledAddresses =
            Svc.PluginInterface.GetIpcSubscriber<List<nint>>("LightlessSync.GetHandledAddresses");
        _snowcloakSyncHandledAddresses =
            Svc.PluginInterface.GetIpcSubscriber<List<nint>>("Snowcloak.GetHandledAddresses");
    }

    // Manual IPC subscribers for different plugins
    private ICallGateSubscriber<List<nint>>? _lightlessSyncHandledAddresses;
    private ICallGateSubscriber<List<nint>>? _snowcloakSyncHandledAddresses;

    private bool IsPluginActive(string pluginKey)
    {
        if (!_marePlugins.TryGetValue(pluginKey, out var pluginInfo)) return false;

        pluginInfo.IsAvailable = DalamudReflector.TryGetDalamudPlugin(pluginKey, out _, false, true);
        return pluginInfo.IsAvailable;
    }

    public override bool IsReady()
        => _marePlugins.Keys.Any(name => DalamudReflector.TryGetDalamudPlugin(name, out _, false, true));

    public Dictionary<string, bool> GetMarePluginStatus()
    {
        foreach (var kvp in _marePlugins)
            kvp.Value.IsAvailable = DalamudReflector.TryGetDalamudPlugin(kvp.Key, out _, false, true);
        return _marePlugins.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.IsAvailable);
    }

    public void SetUiOpen(bool isOpen)
    {
        if (_isUiOpen == isOpen) return;
        _isUiOpen = isOpen;

        PluginLog.Debug($"UI {(isOpen ? "opened" : "closed")}");
    }


    public List<ICharacter> GetPairedPlayers()
    {
        if (!_isUiOpen || !IsReady()) return new List<ICharacter>();

        var pairedAddresses = GetCurrentPairedAddresses();

        // Convert to ICharacter objects
        var result = pairedAddresses
            .Select(addr => Svc.Objects.FirstOrDefault(obj => obj.Address == addr))
            .OfType<ICharacter>()
            .Where(c => c.IsValid())
            .ToList();

        return result;
    }

    public bool IsHandledAddress(nint address)
    {
        var pairedAddresses = GetCurrentPairedAddresses();
        return pairedAddresses.Contains(address);
    }

    private HashSet<nint> GetCurrentPairedAddresses()
    {
        var pairedAddresses = new HashSet<nint>();

        // Update plugin availability first
        foreach (var kvp in _marePlugins)
            kvp.Value.IsAvailable = DalamudReflector.TryGetDalamudPlugin(kvp.Key, out _, false, true);

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

        if (_snowcloakSyncHandledAddresses?.HasFunction == true)
            try
            {
                var addresses = _snowcloakSyncHandledAddresses.InvokeFunc();
                foreach (var addr in addresses) pairedAddresses.Add(addr);
            }
            catch (Exception ex)
            {
                PluginLog.Debug($"Failed to get SnowcloakSync handled addresses: {ex.Message}");
            }

        if (IsPluginActive("MareSempiterne"))
        {
            try
            {
                var pluginInfo = _marePlugins["MareSempiterne"];
                foreach (var addr in GetPlayerSyncAddressesViaPairs(pluginInfo))
                    pairedAddresses.Add(addr);
            }
            catch (Exception ex)
            {
                PluginLog.Debug($"Failed to reflect PlayerSync pair addresses: {ex.Message}");
            }
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
        if (!IsPluginActive("Snowcloak")) return false;

        try
        {
            // Prefer explicit Snowcloak label first
            if (_snowcloakSyncHandledAddresses?.HasFunction == true)
            {
                var addresses = _snowcloakSyncHandledAddresses.InvokeFunc();
                if (addresses.Contains(address)) return true;
            }

            // Do not read Mare label to avoid ambiguity
        }
        catch (Exception ex)
        {
            PluginLog.Debug($"Failed Snowcloak address check: {ex.Message}");
        }

        return false;
    }


    public bool IsAddressHandledByPlayerSync(nint address)
    {
        if (!IsPluginActive("MareSempiterne")) return false;

        try
        {
            var pluginInfo = _marePlugins["MareSempiterne"];
            var viaPairs = GetPlayerSyncAddressesViaPairs(pluginInfo);
            if (viaPairs.Contains(address)) return true;

            // Do not read Mare label to avoid ambiguity
        }
        catch (Exception ex)
        {
            PluginLog.Debug($"Failed Player Sync address check: {ex.Message}");
        }

        return false;
    }

    private HashSet<nint> GetHandledAddressesViaReflection(MarePluginInfo pluginInfo)
    {
        var results = new HashSet<nint>();
        try
        {
            if (!pluginInfo.IsAvailable)
                return results;

            // Re-acquire the service provider each time in case plugin reloaded
            if (!DalamudReflector.TryGetDalamudPlugin(pluginInfo.PluginName, out var marePlugin, true, true))
                return results;

            var host = marePlugin.GetFoP("_host");
            if (host?.GetFoP("Services") is not IServiceProvider serviceProvider)
                return results;

            var assembly = marePlugin.GetType().Assembly;
            var ipcProviderType = assembly.GetType($"{pluginInfo.NamespacePrefix}.Interop.Ipc.IpcProvider");
            if (ipcProviderType == null)
                return results;

            var ipcProvider = serviceProvider.GetService(ipcProviderType);
            if (ipcProvider == null)
                return results;

            var activeHandlersField = ipcProviderType.GetField("_activeGameObjectHandlers", BindingFlags.Instance | BindingFlags.NonPublic);
            if (activeHandlersField?.GetValue(ipcProvider) is not IEnumerable handlers)
                return results;

            foreach (var handler in handlers)
            {
                var addrObj = handler.GetFoP("Address");
                if (addrObj is IntPtr ip && ip != IntPtr.Zero)
                    results.Add((nint)ip);
                else if (addrObj is nint np && np != nint.Zero)
                    results.Add(np);
            }
        }
        catch (Exception e)
        {
            PluginLog.Debug($"[Mare IPC] Reflection for handled addresses failed in {pluginInfo.PluginName}: {e.Message}");
        }

        return results;
    }

    private HashSet<nint> GetPlayerSyncAddressesViaPairs(MarePluginInfo pluginInfo)
    {
        var results = new HashSet<nint>();
        try
        {
            if (!pluginInfo.IsAvailable)
                return results;

            if (pluginInfo.Plugin == null || pluginInfo.PairManager == null)
                InitializeAllPlugins();

            if (pluginInfo.PairManager == null)
                return results;

            var pairManager = pluginInfo.PairManager;
            var pmType = pairManager.GetType();

            var getOnlinePairs = pmType.GetMethod("GetOnlineUserPairs", BindingFlags.Instance | BindingFlags.Public);
            if (getOnlinePairs == null)
                return results;

            if (getOnlinePairs.Invoke(pairManager, null) is not IEnumerable onlinePairs)
                return results;

            foreach (var pair in onlinePairs)
            {
                var pairType = pair.GetType();
                var hasCachedObj = pairType.GetProperty("HasCachedPlayer", BindingFlags.Instance | BindingFlags.Public)?.GetValue(pair);
                var isVisibleObj = pairType.GetProperty("IsVisible", BindingFlags.Instance | BindingFlags.Public)?.GetValue(pair);
                bool hasCached = hasCachedObj is bool b1 && b1;
                bool isVisible = isVisibleObj is bool b2 && b2;
                if (!hasCached) continue;

                var cachedPlayer = pairType.GetProperty("CachedPlayer", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(pair);
                if (cachedPlayer == null) continue;

                var addrObj = cachedPlayer.GetType().GetProperty("PlayerCharacter", BindingFlags.Instance | BindingFlags.Public)?.GetValue(cachedPlayer);
                if (addrObj is nint np && np != nint.Zero)
                {
                    if (isVisible)
                        results.Add(np);
                }
            }
        }
        catch (Exception e)
        {
            PluginLog.Debug($"[Mare IPC] PairManager reflection failed for {pluginInfo.PluginName}: {e.Message}");
        }

        return results;
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

                PluginLog.Information($"[Mare IPC] {pluginName} initialization complete.");
            }
            catch (Exception e)
            {
                PluginLog.Error($"[Mare IPC] An exception occurred during {pluginName} initialization: {e}");
                pluginInfo.IsAvailable = false;
                pluginInfo.Plugin = null;
            }
        }
    }

    public override void HandlePluginListChanged(IEnumerable<string> affectedPluginNames)
    {
        // Check if any of the plugins we manage (LightlessSync, Snowcloak, Player Sync) were affected
        foreach (var name in affectedPluginNames)
        {
            if (_marePlugins.TryGetValue(name, out var info))
            {
                PluginLog.Debug($"[Mare IPC] Managed plugin {name} was affected. Resetting cache.");
                ResetPluginInfo(info);
            }
        }

        if (affectedPluginNames.Intersect(_marePlugins.Keys).Any())
        {
            var isAvailable = IsReady();
            if (isAvailable != _wasAvailable)
            {
                PluginLog.Information(
                    $"[{string.Join("/", _marePlugins.Keys)} IPC] A managed plugin's state changed via plugin list event: {_wasAvailable} -> {isAvailable}");
                // OnPluginStateChanged calls ResetAll, but strictly speaking we only need to update the availability flag here
                // since we already reset the specific plugin info above.
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
            ResetPluginInfo(pluginInfo);
        }
    }

    private void ResetPluginInfo(MarePluginInfo pluginInfo)
    {
        pluginInfo.IsAvailable = false;
        pluginInfo.Plugin = null;
        pluginInfo.PairManager = null;
    }
}
