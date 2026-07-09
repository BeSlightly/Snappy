using System.Collections;
using System.Reflection;
using ECommons.Reflection;

namespace Snappy.Integrations;

public sealed partial class MareIpc
{
    public object? GetCharacterData(ICharacter character)
    {
        if (Svc.Framework.IsInFrameworkUpdateThread)
            return GetCharacterDataOnFrameworkThread(character);

        return Svc.Framework.RunOnFrameworkThread(() => GetCharacterDataOnFrameworkThread(character))
            .GetAwaiter().GetResult();
    }

    private object? GetCharacterDataOnFrameworkThread(ICharacter character)
    {
        RefreshPluginAvailability();

        var availablePlugins = _marePlugins.Values.Where(p => p.IsAvailable).ToList();
        if (!availablePlugins.Any())
        {
            PluginLog.Debug($"No Mare plugins available when trying to get data for {character.Name.TextValue}");
            return null;
        }

        InitializeAllPlugins();

        foreach (var marePlugin in availablePlugins)
        {
            var pairObject = GetMarePairFromPlugin(character, marePlugin);
            if (pairObject == null) continue;

            var characterData = pairObject.GetFoP("LastReceivedCharacterData");
            if (characterData == null)
            {
                var handler = pairObject.GetFoP("Handler");
                if (handler != null)
                    characterData = handler.GetFoP("LastReceivedCharacterData");
            }
            if (characterData != null)
            {
                PluginLog.Debug(
                    $"Successfully retrieved Mare data for {character.Name.TextValue} from {marePlugin.PluginName}");
                return characterData;
            }
        }

        PluginLog.Debug($"No Mare pair found for character {character.Name.TextValue} in any available plugin.");
        return null;
    }

    private IEnumerable<object> EnumeratePairsFromPlugin(MarePluginInfo pluginInfo)
    {
        if (!pluginInfo.IsAvailable || pluginInfo.PairManager == null)
            yield break;

        var pairManager = pluginInfo.PairManager;

        foreach (var probe in GetPairProbeOrder(pluginInfo))
            foreach (var pair in EnumeratePairsFromResult(InvokePairProbe(pairManager, pluginInfo.PluginName, probe)))
                yield return pair;
    }

    private bool TryEnumeratePairsFromPlugin(MarePluginInfo pluginInfo, out IReadOnlyList<object> pairs)
    {
        var results = new List<object>();
        var successfullyEnumerated = false;
        foreach (var probe in GetPairProbeOrder(pluginInfo))
        {
            var probeResult = InvokePairProbe(pluginInfo.PairManager!, pluginInfo.PluginName, probe);
            if (!CanEnumeratePairs(probeResult))
                continue;

            successfullyEnumerated = true;
            results.AddRange(EnumeratePairsFromResult(probeResult));
        }

        pairs = results;
        return successfullyEnumerated;
    }

    private object? GetMarePairFromPlugin(ICharacter character, MarePluginInfo pluginInfo)
    {
        try
        {
            foreach (var pairObject in EnumeratePairsFromPlugin(pluginInfo))
            {
                if (IsMatchingPairObject(pairObject, character))
                    return pairObject;

                var handler = pairObject.GetFoP("Handler");
                if (handler != null && IsMatchingPairObject(handler, character))
                    return handler;
            }
        }
        catch (Exception e)
        {
            PluginLog.Error(
                $"An exception occurred while processing {pluginInfo.PluginName} pairs to find a specific pair.\n{e}");
        }

        return null;
    }

    private static bool IsMatchingPairObject(object pairObject, ICharacter character)
    {
        var address = GetPairAddress(pairObject);
        if (address == character.Address)
            return true;

        var handlerName = GetPairName(pairObject);
        return !string.IsNullOrEmpty(handlerName)
            && string.Equals(handlerName, character.Name.TextValue, StringComparison.Ordinal);
    }

    private static IEnumerable<string> GetPairProbeOrder(MarePluginInfo pluginInfo)
    {
        return pluginInfo.NamespacePrefix switch
        {
            LightlessSyncPluginKey => ["EnumeratePairs"],
            SnowcloakPluginKey => ["GetOnlineUserPairs", "_allClientPairs"],
            MareSynchronosNamespacePrefix => ["GetOnlineUserPairs", "_allClientPairs"],
            _ => ["GetOnlineUserPairs", "GetAllPairObjects", "EnumeratePairs", "_allClientPairs"]
        };
    }

    private static object? InvokePairProbe(object pairManager, string pluginName, string probeName)
    {
        return probeName == "_allClientPairs"
            ? GetAllClientPairsField(pairManager, pluginName)
            : InvokePairManagerMethod(pairManager, probeName);
    }

    private static bool CanEnumeratePairs(object? pairsContainer)
        => pairsContainer is IDictionary || pairsContainer is IEnumerable and not string;

    private static nint GetPairAddress(object pairObject)
    {
        var directAddress = GetAddressFromObject(pairObject);
        if (directAddress != nint.Zero)
            return directAddress;

        // Snowcloak keeps the address on the pair's private cached handler.
        foreach (var memberName in new[] { "CachedPlayer", "Handler" })
        {
            var handler = pairObject.GetFoP(memberName);
            if (handler == null || ReferenceEquals(handler, pairObject))
                continue;

            var handlerAddress = GetAddressFromObject(handler);
            if (handlerAddress != nint.Zero)
                return handlerAddress;
        }

        return nint.Zero;
    }

    private static nint GetAddressFromObject(object pairObject)
    {
        foreach (var propertyName in new[] { "PlayerCharacter", "Address" })
        {
            var addrObj = pairObject.GetFoP(propertyName);
            if (addrObj is nint ptr)
                return ptr;
            if (addrObj is IntPtr iptr)
                return iptr;
        }

        return nint.Zero;
    }

    private static string? GetPairName(object pairObject)
    {
        if (pairObject.GetFoP("PlayerName") is string playerName)
            return playerName;

        try
        {
            var getPlayerName = pairObject.GetType().GetMethod("GetPlayerName",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);
            return getPlayerName?.Invoke(pairObject, null) as string;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsVisibleConnectedPairObject(object pairObject)
    {
        return pairObject.GetFoP("IsVisible") is true && IsConnectedPairObject(pairObject);
    }

    private static bool IsConnectedPairObject(object pairObject)
    {
        if (pairObject.GetFoP("IsPaired") is bool isPaired)
            return isPaired;

        try
        {
            var method = pairObject.GetType().GetMethod("HasAnyConnection",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);

            return method?.Invoke(pairObject, null) is true;
        }
        catch
        {
            return false;
        }
    }

    private static object? InvokePairManagerMethod(object pairManager, string methodName)
    {
        try
        {
            var method = pairManager.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
            return method?.Invoke(pairManager, null);
        }
        catch
        {
            return null;
        }
    }

    private static object? GetAllClientPairsField(object pairManager, string pluginName)
    {
        try
        {
            var field = pairManager.GetType().GetField("_allClientPairs",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return field?.GetValue(pairManager);
        }
        catch (Exception e)
        {
            PluginLog.Debug(
                $"[Mare IPC] Failed to inspect _allClientPairs for {pluginName}: {e.Message}");
            return null;
        }
    }

    private static IEnumerable<object> EnumeratePairsFromResult(object? pairsContainer)
    {
        if (pairsContainer == null) yield break;

        if (pairsContainer is IDictionary dict)
        {
            foreach (var value in dict.Values)
                if (value != null)
                    yield return value;
            yield break;
        }

        if (pairsContainer is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                if (item == null) continue;

                if (item is DictionaryEntry entry)
                {
                    if (entry.Value != null)
                        yield return entry.Value;
                    continue;
                }

                var valueProp = item.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
                if (valueProp != null && valueProp.GetIndexParameters().Length == 0)
                {
                    var value = valueProp.GetValue(item);
                    if (value != null)
                    {
                        yield return value;
                        continue;
                    }
                }

                yield return item;
            }
        }
    }
}
