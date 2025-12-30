using System.Collections;
using System.Reflection;
using ECommons.Reflection;

namespace Snappy.Integrations;

public sealed partial class MareIpc
{
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

        if (IsPluginActive(MareSempiternePluginKey))
        {
            try
            {
                var pluginInfo = _marePlugins[MareSempiternePluginKey];
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
        if (!IsPluginActive(SnowcloakPluginKey)) return false;

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
        if (!IsPluginActive(MareSempiternePluginKey)) return false;

        try
        {
            var pluginInfo = _marePlugins[MareSempiternePluginKey];
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

            var activeHandlersField =
                ipcProviderType.GetField("_activeGameObjectHandlers", BindingFlags.Instance | BindingFlags.NonPublic);
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
                var hasCachedObj =
                    pairType.GetProperty("HasCachedPlayer", BindingFlags.Instance | BindingFlags.Public)?.GetValue(pair);
                var isVisibleObj =
                    pairType.GetProperty("IsVisible", BindingFlags.Instance | BindingFlags.Public)?.GetValue(pair);
                bool hasCached = hasCachedObj is bool b1 && b1;
                bool isVisible = isVisibleObj is bool b2 && b2;
                if (!hasCached) continue;

                var cachedPlayer =
                    pairType.GetProperty("CachedPlayer", BindingFlags.Instance | BindingFlags.NonPublic)
                        ?.GetValue(pair);
                if (cachedPlayer == null) continue;

                var addrObj = cachedPlayer.GetType()
                    .GetProperty("PlayerCharacter", BindingFlags.Instance | BindingFlags.Public)
                    ?.GetValue(cachedPlayer);
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
}
