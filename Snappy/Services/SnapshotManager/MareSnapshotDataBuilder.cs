using System.Collections;
using ECommons.Reflection;

namespace Snappy.Services.SnapshotManager;

public sealed class MareSnapshotDataBuilder
{
    private const int PlayerObjectKind = 0;

    private readonly IIpcManager _ipcManager;

    public MareSnapshotDataBuilder(IIpcManager ipcManager)
    {
        _ipcManager = ipcManager;
    }

    public SnapshotData? BuildFromMare(ICharacter character)
    {
        PluginLog.Debug($"Building snapshot for other player from Mare data: {character.Name.TextValue}");
        var mareCharaData = _ipcManager.GetCharacterDataFromMare(character);
        if (mareCharaData == null)
        {
            Notify.Error($"Could not get Mare data for {character.Name.TextValue}.");
            return null;
        }

        var newManipulation = GetStringProperty(mareCharaData, "ManipulationString", "ManipulationData");
        var newGlamourer = GetPlayerStringFromDictionary(mareCharaData, "GlamourerString", "GlamourerData");
        var remoteB64Customize =
            GetPlayerStringFromDictionary(mareCharaData, "CustomizePlusScale", "CustomizePlusData");

        var newCustomize = string.Empty;
        if (!string.IsNullOrEmpty(remoteB64Customize))
        {
            try
            {
                newCustomize = Encoding.UTF8.GetString(Convert.FromBase64String(remoteB64Customize));
            }
            catch (Exception ex)
            {
                PluginLog.Warning(
                    $"Failed to decode Customize+ data from Mare for {character.Name.TextValue}: {ex.Message}");
            }
        }

        var newFileReplacements = new Dictionary<string, string>();
        if (TryGetPlayerDictionaryValue(mareCharaData.GetFoP("FileReplacements"), out var fileReplacements)
            && fileReplacements is IEnumerable fileList)
        {
            foreach (var fileData in fileList)
            {
                var hash = GetStringProperty(fileData, "Hash");
                if (string.IsNullOrEmpty(hash))
                    continue;

                foreach (var path in GetStringEnumerable(fileData.GetFoP("GamePaths")))
                    newFileReplacements[path] = hash;
            }
        }

        return new SnapshotData(newGlamourer, newCustomize, newManipulation, newFileReplacements,
            new Dictionary<string, string>());
    }

    private static string GetStringProperty(object source, params string[] names)
    {
        foreach (var name in names)
            if (source.GetFoP(name) is string value)
                return value;

        return string.Empty;
    }

    private static string GetPlayerStringFromDictionary(object source, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetPlayerDictionaryValue(source.GetFoP(name), out var value))
                continue;

            if (value is string stringValue)
                return stringValue;
        }

        return string.Empty;
    }

    private static bool TryGetPlayerDictionaryValue(object? dictionaryObject, out object? value)
    {
        value = null;
        if (dictionaryObject is not IDictionary dictionary)
            return false;

        foreach (var key in dictionary.Keys)
        {
            if (!IsPlayerObjectKind(key))
                continue;

            value = dictionary[key];
            return true;
        }

        return false;
    }

    private static bool IsPlayerObjectKind(object? key)
    {
        if (key == null)
            return false;

        try
        {
            return Convert.ToInt32(key, CultureInfo.InvariantCulture) == PlayerObjectKind;
        }
        catch
        {
            return string.Equals(key.ToString(), "Player", StringComparison.Ordinal);
        }
    }

    private static IEnumerable<string> GetStringEnumerable(object? value)
    {
        if (value is string singlePath)
        {
            yield return singlePath;
            yield break;
        }

        if (value is not IEnumerable paths)
            yield break;

        foreach (var item in paths)
            if (item is string path && !string.IsNullOrEmpty(path))
                yield return path;
    }
}
