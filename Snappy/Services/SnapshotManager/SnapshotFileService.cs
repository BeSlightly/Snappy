using System.Collections;
using ECommons.Reflection;
using Snappy.Common;

namespace Snappy.Services.SnapshotManager;

public class SnapshotFileService : ISnapshotFileService
{
    private readonly Configuration _configuration;
    private readonly IIpcManager _ipcManager;
    private readonly ISnapshotIndexService _snapshotIndexService;

    public SnapshotFileService(Configuration configuration, IIpcManager ipcManager,
        ISnapshotIndexService snapshotIndexService)
    {
        _configuration = configuration;
        _ipcManager = ipcManager;
        _snapshotIndexService = snapshotIndexService;
    }

    public async Task<string?> UpdateSnapshotAsync(ICharacter character, bool isSelf,
        Dictionary<string, HashSet<string>>? penumbraReplacements)
    {
        if (!character.IsValid())
        {
            Notify.Error("Invalid character selected for snapshot update.");
            return null;
        }

        var snapshotData = isSelf
            ? await BuildSnapshotFromLocalPlayer(character, penumbraReplacements!)
            : BuildSnapshotFromMareData(character);

        if (snapshotData == null) return null;

        var charaName = character.Name.TextValue;
        var snapshotPath = _snapshotIndexService.FindSnapshotPathForActor(character) ??
                           Path.Combine(_configuration.WorkingDirectory, charaName);

        var paths = SnapshotPaths.From(snapshotPath);
        Directory.CreateDirectory(paths.RootPath);
        Directory.CreateDirectory(paths.FilesDirectory);

        var isNewSnapshot = !File.Exists(paths.SnapshotFile);

        var snapshotInfo = await JsonUtil.DeserializeAsync<SnapshotInfo>(paths.SnapshotFile) ??
                           new SnapshotInfo { SourceActor = charaName };

        // Try to populate SourceWorldId from the live actor if available
        if (snapshotInfo.SourceWorldId == null || snapshotInfo.SourceWorldId <= 0)
            try
            {
                if (character is Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter pc)
                {
                    var worldId = (int)pc.HomeWorld.RowId;
                    if (worldId > 0)
                        snapshotInfo.SourceWorldId = worldId;
                }
            }
            catch
            {
                // HomeWorld may not be available (e.g., GPose actor or non-player)
            }

        var glamourerHistory = await JsonUtil.DeserializeAsync<GlamourerHistory>(paths.GlamourerHistoryFile) ??
                               new GlamourerHistory();
        var customizeHistory = await JsonUtil.DeserializeAsync<CustomizeHistory>(paths.CustomizeHistoryFile) ??
                               new CustomizeHistory();

        foreach (var (gamePath, hash) in snapshotData.FileReplacements)
        {
            snapshotInfo.FileReplacements[gamePath] = hash;

            var hashedFilePath = paths.GetHashedFilePath(hash);
            if (!File.Exists(hashedFilePath))
            {
                var sourceFile = isSelf ? snapshotData.ResolvedPaths[hash] : _ipcManager.GetMareFileCachePath(hash);

                if (!string.IsNullOrEmpty(sourceFile) && File.Exists(sourceFile))
                    await Task.Run(() => File.Copy(sourceFile, hashedFilePath, true));
                else
                    PluginLog.Warning($"Could not find source file for {gamePath} (hash: {hash}).");
            }
        }

        snapshotInfo.ManipulationString = snapshotData.Manipulation;

        var lastGlamourerEntry = glamourerHistory.Entries.LastOrDefault();
        if (lastGlamourerEntry == null || lastGlamourerEntry.GlamourerString != snapshotData.Glamourer)
        {
            var now = DateTime.UtcNow;
            var newEntry = GlamourerHistoryEntry.Create(snapshotData.Glamourer,
                $"Glamourer Update - {now:yyyy-MM-dd HH:mm:ss} UTC");
            glamourerHistory.Entries.Add(newEntry);
            PluginLog.Debug("New Glamourer version detected. Appending to history.");
        }

        var b64Customize = string.IsNullOrEmpty(snapshotData.Customize)
            ? ""
            : Convert.ToBase64String(Encoding.UTF8.GetBytes(snapshotData.Customize));
        var lastCustomizeEntry = customizeHistory.Entries.LastOrDefault();
        if ((lastCustomizeEntry == null || lastCustomizeEntry.CustomizeData != b64Customize) &&
            !string.IsNullOrEmpty(b64Customize))
        {
            var now = DateTime.UtcNow;
            var newEntry = CustomizeHistoryEntry.CreateFromBase64(b64Customize, snapshotData.Customize,
                $"Customize+ Update - {now:yyyy-MM-dd HH:mm:ss} UTC");
            customizeHistory.Entries.Add(newEntry);
            PluginLog.Debug("New Customize+ version detected. Appending to history.");
        }

        snapshotInfo.LastUpdate = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

        SaveSnapshotToDisk(snapshotPath, snapshotInfo, glamourerHistory, customizeHistory);

        if (isNewSnapshot)
            Notify.Success($"New snapshot for '{charaName}' created successfully.");
        else
            Notify.Success($"Snapshot for '{charaName}' updated successfully.");

        return snapshotPath;
    }

    public void RenameSnapshot(string oldPath, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            Notify.Error("New snapshot name cannot be empty.");
            return;
        }

        try
        {
            var parent = Path.GetDirectoryName(oldPath)!;
            var newPath = Path.Combine(parent, newName);
            if (Directory.Exists(newPath))
            {
                Notify.Error("A directory with that name already exists.");
                return;
            }

            var oldName = Path.GetFileName(oldPath);
            Directory.Move(oldPath, newPath);
            Notify.Success($"Snapshot '{oldName}' renamed to '{newName}'.");
        }
        catch (Exception e)
        {
            Notify.Error($"Could not rename snapshot.\n{e.Message}");
            PluginLog.Error($"Could not rename snapshot: {e}");
        }
    }

    public void SaveSnapshotToDisk(string snapshotPath, SnapshotInfo info, GlamourerHistory glamourerHistory,
        CustomizeHistory customizeHistory)
    {
        var paths = SnapshotPaths.From(snapshotPath);
        JsonUtil.Serialize(info, paths.SnapshotFile);
        JsonUtil.Serialize(glamourerHistory, paths.GlamourerHistoryFile);
        JsonUtil.Serialize(customizeHistory, paths.CustomizeHistoryFile);
    }

    private async Task<SnapshotData?> BuildSnapshotFromLocalPlayer(ICharacter character,
        Dictionary<string, HashSet<string>> penumbraReplacements)
    {
        PluginLog.Debug($"Building snapshot for local player: {character.Name.TextValue}");
        var newGlamourer = _ipcManager.GetGlamourerState(character);
        var newCustomize = _ipcManager.GetCustomizePlusScale(character);
        var newManipulation = _ipcManager.GetMetaManipulations(character.ObjectIndex);
        var newFileReplacements = new Dictionary<string, string>();
        var resolvedPaths = new Dictionary<string, string>();

        foreach (var (resolvedPath, gamePaths) in penumbraReplacements)
        {
            if (!File.Exists(resolvedPath))
                continue;

            var fileBytes = await File.ReadAllBytesAsync(resolvedPath);
            var hash = PluginUtil.GetFileHash(fileBytes);
            resolvedPaths[hash] = resolvedPath;
            foreach (var gamePath in gamePaths) newFileReplacements[gamePath] = hash;
        }

        return new SnapshotData(newGlamourer, newCustomize, newManipulation, newFileReplacements, resolvedPaths);
    }

    private SnapshotData? BuildSnapshotFromMareData(ICharacter character)
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

        var newCustomize = string.IsNullOrEmpty(remoteB64Customize)
            ? string.Empty
            : Encoding.UTF8.GetString(Convert.FromBase64String(remoteB64Customize));

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

    private record SnapshotData(
        string Glamourer,
        string Customize,
        string Manipulation,
        Dictionary<string, string> FileReplacements,
        Dictionary<string, string> ResolvedPaths);
}