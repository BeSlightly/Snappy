using System.Collections;
using System.Reflection;
using ECommons.Reflection;

namespace Snappy.Integrations;

public sealed partial class MareIpc
{
    public object? GetCharacterData(ICharacter character)
    {
        RefreshPluginAvailability();

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
        }).Result;
    }

    private IEnumerable<object> EnumeratePairsFromPlugin(MarePluginInfo pluginInfo)
    {
        if (!pluginInfo.IsAvailable || pluginInfo.PairManager == null)
            yield break;

        var pairManager = pluginInfo.PairManager;

        foreach (var pair in EnumeratePairsFromResult(InvokePairManagerMethod(pairManager, "GetOnlineUserPairs")))
            yield return pair;

        foreach (var pair in EnumeratePairsFromResult(InvokePairManagerMethod(pairManager, "GetAllPairObjects")))
            yield return pair;

        foreach (var pair in EnumeratePairsFromResult(GetAllClientPairsField(pairManager, pluginInfo.PluginName)))
            yield return pair;
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
        var addrObj = pairObject.GetFoP("PlayerCharacter");
        if (addrObj is nint ptr && ptr == character.Address)
            return true;
        if (addrObj is IntPtr iptr && iptr == character.Address)
            return true;

        var handlerName = pairObject.GetFoP("PlayerName") as string;
        return !string.IsNullOrEmpty(handlerName)
            && string.Equals(handlerName, character.Name.TextValue, StringComparison.Ordinal);
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
            return pairManager.GetFoP("_allClientPairs");
        }
        catch (Exception e)
        {
            PluginLog.Error(
                $"An exception occurred while reflecting into {pluginName} to get client pairs.\n{e}");
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
