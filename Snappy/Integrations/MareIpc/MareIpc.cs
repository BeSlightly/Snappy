using System.Reflection;
using Dalamud.Plugin.Ipc;
using ECommons.Reflection;
using Snappy.Services;

namespace Snappy.Integrations;

public sealed partial class MareIpc : IpcSubscriber
{
    private const string LightlessSyncPluginKey = "LightlessSync";
    private const string SnowcloakPluginKey = "Snowcloak";
    private const string MareSempiternePluginKey = "MareSempiterne";
    private const string PlayerSyncDisplayName = "Player Sync";
    private const string MareSynchronosNamespacePrefix = "MareSynchronos";
    private const string LightlessHandledAddressesIpc = "LightlessSync.GetHandledAddresses";
    private const string SnowcloakHandledAddressesIpc = "Snowcloak.GetHandledAddresses";

    private bool _isUiOpen;

    // Multi-Mare support
    private readonly Dictionary<string, MarePluginInfo> _marePlugins = new()
    {
        { LightlessSyncPluginKey, new MarePluginInfo(LightlessSyncPluginKey, LightlessSyncPluginKey) },
        { SnowcloakPluginKey, new MarePluginInfo(SnowcloakPluginKey, SnowcloakPluginKey) },
        { MareSempiternePluginKey, new MarePluginInfo(PlayerSyncDisplayName, MareSynchronosNamespacePrefix) }
    };

    private class MarePluginInfo
    {
        public string PluginName { get; }
        public string NamespacePrefix { get; }
        public bool IsAvailable { get; set; }
        public object? Plugin { get; set; }
        public object? PairManager { get; set; }
        public object? PairLedger { get; set; }
        public object? FileCacheManager { get; set; }
        public MethodInfo? GetFileCacheByHashMethod { get; set; }

        public MarePluginInfo(string pluginName, string namespacePrefix)
        {
            PluginName = pluginName;
            NamespacePrefix = namespacePrefix;
        }
    }

    public MareIpc() : base(LightlessSyncPluginKey)
    {
        // Initialize manual IPC subscribers
        _lightlessSyncHandledAddresses =
            Svc.PluginInterface.GetIpcSubscriber<List<nint>>(LightlessHandledAddressesIpc);
        _snowcloakSyncHandledAddresses =
            Svc.PluginInterface.GetIpcSubscriber<List<nint>>(SnowcloakHandledAddressesIpc);
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
    {
        RefreshPluginAvailability();
        return _marePlugins.Values.Any(plugin => plugin.IsAvailable);
    }

    public Dictionary<string, bool> GetMarePluginStatus()
    {
        RefreshPluginAvailability();
        return _marePlugins.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.IsAvailable);
    }

    public void SetUiOpen(bool isOpen)
    {
        if (_isUiOpen == isOpen) return;
        _isUiOpen = isOpen;

        PluginLog.Debug($"UI {(isOpen ? "opened" : "closed")}");
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

                var pairLedgerType = marePlugin.GetType().Assembly
                    .GetType($"{pluginInfo.NamespacePrefix}.PlayerData.Pairs.PairLedger");
                if (pairLedgerType != null)
                {
                    pluginInfo.PairLedger = serviceProvider.GetService(pairLedgerType);
                    if (pluginInfo.PairLedger == null)
                        PluginLog.Warning($"[Mare IPC] Could not get PairLedger service for {pluginName}.");
                }

                var fileCacheManagerType = marePlugin.GetType().Assembly
                    .GetType($"{pluginInfo.NamespacePrefix}.FileCache.FileCacheManager");
                if (fileCacheManagerType != null)
                {
                    pluginInfo.FileCacheManager = serviceProvider.GetService(fileCacheManagerType);
                    if (pluginInfo.FileCacheManager != null)
                    {
                        pluginInfo.GetFileCacheByHashMethod = fileCacheManagerType.GetMethod("GetFileCacheByHash",
                            new[] { typeof(string), typeof(bool) });

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
        pluginInfo.PairLedger = null;
        pluginInfo.FileCacheManager = null;
        pluginInfo.GetFileCacheByHashMethod = null;
    }

    private void RefreshPluginAvailability()
    {
        foreach (var (pluginKey, pluginInfo) in _marePlugins)
            pluginInfo.IsAvailable = DalamudReflector.TryGetDalamudPlugin(pluginKey, out _, false, true);
    }
}
