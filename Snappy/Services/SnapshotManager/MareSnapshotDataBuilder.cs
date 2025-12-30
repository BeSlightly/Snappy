using System.Collections;
using ECommons.Reflection;

namespace Snappy.Services.SnapshotManager;

public sealed class MareSnapshotDataBuilder
{
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

        var newManipulation = mareCharaData.GetFoP("ManipulationData") as string ?? string.Empty;

        var glamourerDict = mareCharaData.GetFoP("GlamourerData") as IDictionary;
        var newGlamourer =
            glamourerDict?.Count > 0 && glamourerDict.Keys.Cast<object>().FirstOrDefault(k => (int)k == 0) is
                { } glamourerKey
                ? glamourerDict[glamourerKey] as string ?? string.Empty
                : string.Empty;

        var customizeDict = mareCharaData.GetFoP("CustomizePlusData") as IDictionary;
        var remoteB64Customize =
            customizeDict?.Count > 0 && customizeDict.Keys.Cast<object>().FirstOrDefault(k => (int)k == 0) is
                { } customizeKey
                ? customizeDict[customizeKey] as string ?? string.Empty
                : string.Empty;

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
        var fileReplacementsDict = mareCharaData.GetFoP("FileReplacements") as IDictionary;
        if (fileReplacementsDict != null &&
            fileReplacementsDict.Keys.Cast<object>().FirstOrDefault(k => (int)k == 0) is { } playerKey)
            if (fileReplacementsDict[playerKey] is IEnumerable fileList)
                foreach (var fileData in fileList)
                {
                    var gamePaths = fileData.GetFoP("GamePaths") as string[];
                    var hash = fileData.GetFoP("Hash") as string;
                    if (gamePaths != null && !string.IsNullOrEmpty(hash))
                        foreach (var path in gamePaths)
                            newFileReplacements[path] = hash;
                }

        return new SnapshotData(newGlamourer, newCustomize, newManipulation, newFileReplacements,
            new Dictionary<string, string>());
    }
}
