using System.Reflection;
using Dalamud.Plugin.Ipc;

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

        RefreshPluginAvailability();

        foreach (var addr in GetCurrentLightlessAddresses())
            pairedAddresses.Add(addr);

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

        foreach (var addr in GetCurrentPlayerSyncAddresses())
            pairedAddresses.Add(addr);

        return pairedAddresses;
    }

    public bool IsAddressHandledByLightless(nint address)
    {
        return GetCurrentLightlessAddresses().Contains(address);
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
            return GetCurrentPlayerSyncAddresses().Contains(address);
        }
        catch (Exception ex)
        {
            PluginLog.Debug($"Failed Player Sync address check: {ex.Message}");
        }

        return false;
    }

    private HashSet<nint> GetCurrentLightlessAddresses()
    {
        var handledAddresses = GetHandledAddressesFromIpc(_lightlessSyncHandledAddresses, "LightlessSync");
        var visibleAddresses = _marePlugins.TryGetValue(LightlessSyncPluginKey, out var lightlessPlugin)
            ? GetVisiblePairAddressesViaPairs(lightlessPlugin)
            : null;

        if (visibleAddresses != null)
        {
            if (handledAddresses != null)
                visibleAddresses.IntersectWith(handledAddresses);

            return visibleAddresses;
        }

        return handledAddresses ?? [];
    }

    private HashSet<nint> GetCurrentPlayerSyncAddresses()
    {
        if (!IsPluginActive(MareSempiternePluginKey))
            return [];

        return GetHandledAddressesFromIpc(_playerSyncHandledAddresses, "PlayerSync") ?? [];
    }

    private static HashSet<nint>? GetHandledAddressesFromIpc(ICallGateSubscriber<List<nint>>? subscriber, string pluginName)
    {
        if (subscriber?.HasFunction != true)
            return null;

        try
        {
            return subscriber.InvokeFunc().Where(addr => addr != nint.Zero).ToHashSet();
        }
        catch (Exception ex)
        {
            PluginLog.Debug($"Failed to get {pluginName} handled addresses: {ex.Message}");
            return null;
        }
    }

    private HashSet<nint>? GetVisiblePairAddressesViaPairs(MarePluginInfo pluginInfo)
    {
        try
        {
            if (!pluginInfo.IsAvailable)
                return [];

            if (pluginInfo.Plugin == null || pluginInfo.PairManager == null)
                InitializeAllPlugins();

            if (pluginInfo.PairManager == null)
                return null;

            var results = new HashSet<nint>();
            foreach (var pair in EnumeratePairsFromPlugin(pluginInfo))
            {
                var pairType = pair.GetType();
                var isVisibleObj = pairType.GetProperty("IsVisible", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(pair);
                if (isVisibleObj is not true)
                    continue;

                var addr = GetPairAddress(pair);
                if (addr != nint.Zero)
                    results.Add(addr);
            }

            return results;
        }
        catch (Exception e)
        {
            PluginLog.Debug($"[Mare IPC] Visible pair reflection failed for {pluginInfo.PluginName}: {e.Message}");
            return null;
        }
    }

}
