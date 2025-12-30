using System.Collections;
using System.Reflection;
using ECommons.Reflection;

namespace Snappy.Integrations;

public sealed partial class MareIpc
{
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
        if (pluginInfo.PairLedger != null)
        {
            try
            {
                var getAllEntriesMethod = pluginInfo.PairLedger.GetType()
                    .GetMethod("GetAllEntries", BindingFlags.Instance | BindingFlags.Public);
                if (getAllEntriesMethod?.Invoke(pluginInfo.PairLedger, null) is IEnumerable entries)
                {
                    int entryCount = 0;
                    foreach (var entry in entries)
                    {
                        entryCount++;
                        var handler = entry.GetFoP("Handler");
                        if (handler == null) continue;

                        var addrObj = handler.GetFoP("PlayerCharacter");
                        if (addrObj is nint ptr && ptr == character.Address)
                        {
                            return handler;
                        }
                        if (addrObj is IntPtr iptr && iptr == character.Address)
                        {
                            return handler;
                        }

                        var handlerName = handler.GetFoP("PlayerName") as string;
                        if (!string.IsNullOrEmpty(handlerName) &&
                            string.Equals(handlerName, character.Name.TextValue, StringComparison.Ordinal))
                            return handler;
                    }
                    PluginLog.Debug(
                        $"[Mare IPC] Checked {entryCount} PairLedger entries in {pluginInfo.PluginName}, no match found for {character.Name.TextValue} (Addr: {character.Address:X})");
                }
            }
            catch (Exception e)
            {
                PluginLog.Error(
                    $"An exception occurred while processing {pluginInfo.PluginName} pair ledger entries.\n{e}");
            }
        }

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
}
