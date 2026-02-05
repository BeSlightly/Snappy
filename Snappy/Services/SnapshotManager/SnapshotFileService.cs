using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ECommons.ExcelServices;
using Snappy.Common;
using Snappy.Common.Utilities;
using Snappy.Models;

namespace Snappy.Services.SnapshotManager;

public class SnapshotFileService : ISnapshotFileService
{
    private readonly Configuration _configuration;
    private readonly IIpcManager _ipcManager;
    private readonly PenumbraCollectionSnapshotDataBuilder _collectionSnapshotDataBuilder;
    private readonly LiveSnapshotDataBuilder _liveSnapshotDataBuilder;
    private readonly MareSnapshotDataBuilder _mareSnapshotDataBuilder;
    private readonly ISnapshotIndexService _snapshotIndexService;

    public SnapshotFileService(Configuration configuration, IIpcManager ipcManager,
        ISnapshotIndexService snapshotIndexService)
    {
        _configuration = configuration;
        _ipcManager = ipcManager;
        _collectionSnapshotDataBuilder = new PenumbraCollectionSnapshotDataBuilder(ipcManager);
        _liveSnapshotDataBuilder = new LiveSnapshotDataBuilder(ipcManager);
        _mareSnapshotDataBuilder = new MareSnapshotDataBuilder(ipcManager);
        _snapshotIndexService = snapshotIndexService;
    }

    private static string BuildDefaultSnapshotPath(string workingDirectory, string charaName, int? worldId,
        string? worldName)
    {
        if (!string.IsNullOrWhiteSpace(worldName))
            return Path.Combine(workingDirectory, $"{charaName}@{worldName}");
        if (worldId is > 0)
            return Path.Combine(workingDirectory, $"{charaName}@{worldId}");
        return Path.Combine(workingDirectory, charaName);
    }

    public async Task<string?> UpdateSnapshotAsync(ICharacter character, bool isLocalPlayer,
        Dictionary<string, HashSet<string>>? penumbraReplacements)
    {
        var now = DateTime.UtcNow;

        if (!character.IsValid())
        {
            Notify.Error("Invalid character selected for snapshot update.");
            return null;
        }

        var charaName = character.Name.TextValue;
        var (resolvedWorldId, resolvedWorldName) = ResolveHomeWorld(character, charaName);

        var snapshotPath = _snapshotIndexService.FindSnapshotPathForActor(character) ??
                           BuildDefaultSnapshotPath(_configuration.WorkingDirectory, charaName, resolvedWorldId,
                               resolvedWorldName);

        var useLiveData = _configuration.UseLiveSnapshotData || isLocalPlayer;
        var snapshotData = await BuildSnapshotDataAsync(character, useLiveData, penumbraReplacements);

        if (snapshotData == null) return null;

        if (!useLiveData)
        {
            BackfillSnapshotDataFromPenumbra(character, snapshotData);
        }

        var paths = SnapshotPaths.From(snapshotPath);
        Directory.CreateDirectory(paths.RootPath);
        Directory.CreateDirectory(paths.FilesDirectory);

        var isNewSnapshot = !File.Exists(paths.SnapshotFile);

        var snapshotInfo = await JsonUtil.DeserializeAsync<SnapshotInfo>(paths.SnapshotFile) ??
                           new SnapshotInfo { SourceActor = charaName };
        snapshotInfo.FileMaps ??= new List<FileMapEntry>();

        // Backfill missing per-map manipulation data for older snapshots.
        BackfillManipulationStrings(snapshotInfo);
        UpdateSourceWorld(snapshotInfo, resolvedWorldId, resolvedWorldName);

        var glamourerHistory = await JsonUtil.DeserializeAsync<GlamourerHistory>(paths.GlamourerHistoryFile) ??
                               new GlamourerHistory();
        var customizeHistory = await JsonUtil.DeserializeAsync<CustomizeHistory>(paths.CustomizeHistoryFile) ??
                               new CustomizeHistory();

        var resolvedCurrentMap =
            EnsureBaseFileMap(snapshotInfo, glamourerHistory, customizeHistory, now);

        var includeRemovals = !useLiveData || !_configuration.UsePenumbraIpcResourcePaths;
        var mapChanged = UpdateFileMaps(snapshotInfo, snapshotData, resolvedCurrentMap, includeRemovals, now);

        var useMareFileCache = !_configuration.UseLiveSnapshotData && !isLocalPlayer;
        foreach (var (gamePath, hash) in snapshotData.FileReplacements)
        {
            snapshotInfo.FileReplacements[gamePath] = hash;

            var existingFilePath = paths.FindAnyExistingHashedFilePath(hash);
            var hashedFilePath = existingFilePath ?? paths.GetPreferredHashedFilePath(hash, gamePath);
            if (!File.Exists(hashedFilePath))
            {
                string? sourceFile = null;
                if (useMareFileCache)
                    sourceFile = _ipcManager.GetMareFileCachePath(hash);
                else
                    snapshotData.ResolvedPaths.TryGetValue(hash, out sourceFile);

                if (!string.IsNullOrEmpty(sourceFile) && File.Exists(sourceFile))
                    await Task.Run(() => File.Copy(sourceFile, hashedFilePath, true));
                else
                    PluginLog.Warning($"Could not find source file for {gamePath} (hash: {hash}).");
            }
        }

        snapshotInfo.ManipulationString = snapshotData.Manipulation;

        var b64Customize = string.IsNullOrEmpty(snapshotData.Customize)
            ? ""
            : Convert.ToBase64String(Encoding.UTF8.GetBytes(snapshotData.Customize));
        var lastCustomizeEntry = customizeHistory.Entries.LastOrDefault();
        var lastCustomizeData = lastCustomizeEntry?.CustomizeData ?? string.Empty;
        var customizeChanged = !string.Equals(lastCustomizeData, b64Customize, StringComparison.Ordinal);
        var hasCustomizeData = !string.IsNullOrEmpty(b64Customize);

        var lastGlamourerEntry = glamourerHistory.Entries.LastOrDefault();
        var hasGlamourerData = !string.IsNullOrEmpty(snapshotData.Glamourer);
        var addedGlamourerEntry = false;
        if (hasGlamourerData &&
            (lastGlamourerEntry == null || lastGlamourerEntry.GlamourerString != snapshotData.Glamourer))
        {
            var entryStamp = DateTime.UtcNow;
            var newEntry = GlamourerHistoryEntry.Create(snapshotData.Glamourer,
                $"Glamourer Update - {entryStamp:yyyy-MM-dd HH:mm:ss} UTC", snapshotInfo.CurrentFileMapId,
                b64Customize);
            glamourerHistory.Entries.Add(newEntry);
            PluginLog.Debug("New Glamourer version detected. Appending to history.");
            addedGlamourerEntry = true;
        }
        if (mapChanged && !addedGlamourerEntry &&
            !string.Equals(lastGlamourerEntry?.FileMapId, snapshotInfo.CurrentFileMapId, StringComparison.OrdinalIgnoreCase))
        {
            var glamourerString = hasGlamourerData
                ? snapshotData.Glamourer
                : lastGlamourerEntry?.GlamourerString ?? string.Empty;
            // Glamourer string unchanged but files changed; record a new entry so users can pick the correct file map.
            var entryStamp = DateTime.UtcNow;
            var newEntry = GlamourerHistoryEntry.Create(glamourerString,
                $"Files Update - {entryStamp:yyyy-MM-dd HH:mm:ss} UTC", snapshotInfo.CurrentFileMapId,
                b64Customize);
            glamourerHistory.Entries.Add(newEntry);
            PluginLog.Debug("File map changed without Glamourer change. Added history entry to capture file map.");
            addedGlamourerEntry = true;
        }

        if (customizeChanged && !addedGlamourerEntry &&
            (!string.IsNullOrEmpty(snapshotData.Glamourer) || !string.IsNullOrEmpty(lastGlamourerEntry?.GlamourerString)))
        {
            var glamourerString = hasGlamourerData
                ? snapshotData.Glamourer
                : lastGlamourerEntry?.GlamourerString ?? string.Empty;
            var entryStamp = DateTime.UtcNow;
            var newEntry = GlamourerHistoryEntry.Create(glamourerString,
                $"Customize+ Update - {entryStamp:yyyy-MM-dd HH:mm:ss} UTC", snapshotInfo.CurrentFileMapId,
                b64Customize);
            glamourerHistory.Entries.Add(newEntry);
            PluginLog.Debug("Customize+ changed without Glamourer change. Added history entry to bind C+ state.");
        }

        if (customizeChanged && hasCustomizeData)
        {
            var entryStamp = DateTime.UtcNow;
            var newEntry = CustomizeHistoryEntry.CreateFromBase64(b64Customize, snapshotData.Customize,
                $"Customize+ Update - {entryStamp:yyyy-MM-dd HH:mm:ss} UTC", snapshotInfo.CurrentFileMapId);
            customizeHistory.Entries.Add(newEntry);
            PluginLog.Debug("New Customize+ version detected. Appending to history.");
        }

        snapshotInfo.LastUpdate = now.ToString("o", CultureInfo.InvariantCulture);

        SaveSnapshotToDisk(snapshotPath, snapshotInfo, glamourerHistory, customizeHistory);

        if (isNewSnapshot)
            Notify.Success($"New snapshot for '{charaName}' created successfully.");
        else
            Notify.Success($"Snapshot for '{charaName}' updated successfully.");

        return snapshotPath;
    }

    private async Task<SnapshotData?> BuildSnapshotDataAsync(ICharacter character, bool useLiveData,
        Dictionary<string, HashSet<string>>? penumbraReplacements)
    {
        if (useLiveData)
        {
            return _configuration.UsePenumbraIpcResourcePaths
                ? await _liveSnapshotDataBuilder.BuildAsync(character, penumbraReplacements)
                : await _collectionSnapshotDataBuilder.BuildAsync(character);
        }

        return _mareSnapshotDataBuilder.BuildFromMare(character);
    }

    private static (int? WorldId, string? WorldName) ResolveHomeWorld(ICharacter character, string charaName)
    {
        int? resolvedWorldId = null;
        string? resolvedWorldName = null;

        try
        {
            if (character is Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter pc)
            {
                var worldId = (int)pc.HomeWorld.RowId;
                if (worldId > 0)
                {
                    resolvedWorldId = worldId;
                    var worldName = ExcelWorldHelper.GetName((uint)worldId);
                    if (!string.IsNullOrWhiteSpace(worldName))
                        resolvedWorldName = worldName;
                }
            }
        }
        catch (Exception ex)
        {
            PluginLog.Verbose(
                $"Failed to resolve HomeWorld for {charaName}: {ex.Message}");
        }

        return (resolvedWorldId, resolvedWorldName);
    }

    private static void BackfillManipulationStrings(SnapshotInfo snapshotInfo)
    {
        if (snapshotInfo.FileMaps == null || snapshotInfo.FileMaps.Count == 0)
            return;

        foreach (var entry in snapshotInfo.FileMaps)
            if (entry.ManipulationString == null)
                entry.ManipulationString = snapshotInfo.ManipulationString;
    }

    private static void UpdateSourceWorld(SnapshotInfo snapshotInfo, int? resolvedWorldId, string? resolvedWorldName)
    {
        if (resolvedWorldId is not > 0)
            return;

        if (snapshotInfo.SourceWorldId == null || snapshotInfo.SourceWorldId <= 0
                                                || snapshotInfo.SourceWorldId != resolvedWorldId)
            snapshotInfo.SourceWorldId = resolvedWorldId;
        if (!string.IsNullOrWhiteSpace(resolvedWorldName)
            && !string.Equals(snapshotInfo.SourceWorldName, resolvedWorldName, StringComparison.Ordinal))
            snapshotInfo.SourceWorldName = resolvedWorldName;
    }

    private static Dictionary<string, string> EnsureBaseFileMap(SnapshotInfo snapshotInfo,
        GlamourerHistory glamourerHistory, CustomizeHistory customizeHistory, DateTime now)
    {
        var resolvedCurrentMap = FileMapUtil.ResolveFileMap(snapshotInfo, snapshotInfo.CurrentFileMapId);

        FileMapUtil.CreateBaseMapIfMissing(snapshotInfo, resolvedCurrentMap, now);
        if (snapshotInfo.CurrentFileMapId != null)
        {
            foreach (var entry in glamourerHistory.Entries.Where(e => string.IsNullOrEmpty(e.FileMapId)))
                entry.FileMapId = snapshotInfo.CurrentFileMapId;
            foreach (var entry in customizeHistory.Entries.Where(e => string.IsNullOrEmpty(e.FileMapId)))
                entry.FileMapId = snapshotInfo.CurrentFileMapId;
        }

        return FileMapUtil.ResolveFileMap(snapshotInfo, snapshotInfo.CurrentFileMapId);
    }

    private static bool UpdateFileMaps(SnapshotInfo snapshotInfo, SnapshotData snapshotData,
        Dictionary<string, string> resolvedCurrentMap, bool includeRemovals, DateTime now)
    {
        var incomingFileMap = new Dictionary<string, string>(snapshotData.FileReplacements,
            StringComparer.OrdinalIgnoreCase);
        var mapChanges = FileMapUtil.CalculateChanges(resolvedCurrentMap, incomingFileMap, includeRemovals);
        var fileMapChanged = mapChanges.Any();

        var fileMaps = snapshotInfo.FileMaps ?? new List<FileMapEntry>();
        snapshotInfo.FileMaps = fileMaps;
        var currentMapEntry = fileMaps.FirstOrDefault(m =>
            string.Equals(m.Id, snapshotInfo.CurrentFileMapId, StringComparison.OrdinalIgnoreCase));
        var currentManipulation = currentMapEntry?.ManipulationString ?? snapshotInfo.ManipulationString;
        var manipChanged = !string.Equals(currentManipulation, snapshotData.Manipulation, StringComparison.Ordinal);
        var mapChanged = fileMapChanged || manipChanged;
        if (mapChanged)
        {
            if (currentMapEntry != null && currentMapEntry.ManipulationString == null)
                currentMapEntry.ManipulationString = currentManipulation;

            var newMapId = Guid.NewGuid().ToString("N");
            fileMaps.Add(new FileMapEntry
            {
                Id = newMapId,
                BaseId = snapshotInfo.CurrentFileMapId,
                Changes = mapChanges,
                Timestamp = now.ToString("o", CultureInfo.InvariantCulture),
                ManipulationString = snapshotData.Manipulation
            });
            snapshotInfo.CurrentFileMapId = newMapId;
        }
        else if (currentMapEntry != null && currentMapEntry.ManipulationString == null)
        {
            currentMapEntry.ManipulationString = snapshotData.Manipulation;
        }

        resolvedCurrentMap = FileMapUtil.ResolveFileMap(snapshotInfo, snapshotInfo.CurrentFileMapId);
        snapshotInfo.FileReplacements = new Dictionary<string, string>(resolvedCurrentMap,
            StringComparer.OrdinalIgnoreCase);

        return mapChanged;
    }

    private void BackfillSnapshotDataFromPenumbra(ICharacter character, SnapshotData snapshotData)
    {
        if (snapshotData.FileReplacements.Count == 0)
            return;

        var resolvedByGamePath = _ipcManager.PenumbraGetCollectionResolvedFiles(character.ObjectIndex);
        if (resolvedByGamePath.Count == 0)
        {
            var resourcePaths = _ipcManager.PenumbraGetGameObjectResourcePaths(character.ObjectIndex);
            if (resourcePaths.Count > 0)
            {
                resolvedByGamePath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (resolvedPath, gamePaths) in resourcePaths)
                {
                    foreach (var gamePath in gamePaths)
                    {
                        if (!resolvedByGamePath.ContainsKey(gamePath))
                            resolvedByGamePath[gamePath] = resolvedPath;
                    }
                }
            }
        }

        if (resolvedByGamePath.Count == 0)
            return;

        var hashCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int backfilled = 0;
        int mismatched = 0;

        foreach (var (gamePath, expectedHash) in snapshotData.FileReplacements.ToList())
        {
            if (snapshotData.ResolvedPaths.ContainsKey(expectedHash))
                continue;

            if (!resolvedByGamePath.TryGetValue(gamePath, out var filePath))
                continue;

            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                continue;

            if (!hashCache.TryGetValue(filePath, out var actualHash))
            {
                actualHash = PluginUtil.GetFileHash(filePath);
                if (string.IsNullOrEmpty(actualHash))
                    continue;
                hashCache[filePath] = actualHash;
            }

            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                mismatched++;

            snapshotData.FileReplacements[gamePath] = actualHash;
            if (!snapshotData.ResolvedPaths.ContainsKey(actualHash))
                snapshotData.ResolvedPaths[actualHash] = filePath;

            backfilled++;
        }

        if (backfilled > 0)
        {
            PluginLog.Debug(
                $"[Snappy] Backfilled {backfilled} file(s) from Penumbra for snapshot; hash mismatches: {mismatched}.");
        }
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

}
