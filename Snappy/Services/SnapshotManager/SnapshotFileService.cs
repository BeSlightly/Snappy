using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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

    public async Task<string?> UpdateSnapshotAsync(ICharacter character, bool isLocalPlayer,
        Dictionary<string, HashSet<string>>? penumbraReplacements)
    {
        var now = DateTime.UtcNow;

        if (!character.IsValid())
        {
            Notify.Error("Invalid character selected for snapshot update.");
            return null;
        }

        var useLiveData = _configuration.UseLiveSnapshotData || isLocalPlayer;
        SnapshotData? snapshotData;
        if (useLiveData)
        {
            snapshotData = _configuration.UsePenumbraIpcResourcePaths
                ? await _liveSnapshotDataBuilder.BuildAsync(character, penumbraReplacements)
                : await _collectionSnapshotDataBuilder.BuildAsync(character);
        }
        else
        {
            snapshotData = _mareSnapshotDataBuilder.BuildFromMare(character);
        }

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

        var resolvedCurrentMap = FileMapUtil.ResolveFileMap(snapshotInfo, snapshotInfo.CurrentFileMapId);

        FileMapUtil.CreateBaseMapIfMissing(snapshotInfo, resolvedCurrentMap, now);
        if (snapshotInfo.CurrentFileMapId != null)
        {
            foreach (var entry in glamourerHistory.Entries.Where(e => string.IsNullOrEmpty(e.FileMapId)))
                entry.FileMapId = snapshotInfo.CurrentFileMapId;
            foreach (var entry in customizeHistory.Entries.Where(e => string.IsNullOrEmpty(e.FileMapId)))
                entry.FileMapId = snapshotInfo.CurrentFileMapId;
        }
        resolvedCurrentMap = FileMapUtil.ResolveFileMap(snapshotInfo, snapshotInfo.CurrentFileMapId);

        var incomingFileMap = new Dictionary<string, string>(snapshotData.FileReplacements, StringComparer.OrdinalIgnoreCase);
        var includeRemovals = !useLiveData || !_configuration.UsePenumbraIpcResourcePaths;
        var mapChanges = FileMapUtil.CalculateChanges(resolvedCurrentMap, incomingFileMap, includeRemovals);
        var mapChanged = mapChanges.Any();
        if (mapChanged)
        {
            var newMapId = Guid.NewGuid().ToString("N");
            snapshotInfo.FileMaps.Add(new FileMapEntry
            {
                Id = newMapId,
                BaseId = snapshotInfo.CurrentFileMapId,
                Changes = mapChanges,
                Timestamp = now.ToString("o", CultureInfo.InvariantCulture)
            });
            snapshotInfo.CurrentFileMapId = newMapId;
        }

        resolvedCurrentMap = FileMapUtil.ResolveFileMap(snapshotInfo, snapshotInfo.CurrentFileMapId);
        snapshotInfo.FileReplacements = new Dictionary<string, string>(resolvedCurrentMap,
            StringComparer.OrdinalIgnoreCase);

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

        var lastGlamourerEntry = glamourerHistory.Entries.LastOrDefault();
        var hasGlamourerData = !string.IsNullOrEmpty(snapshotData.Glamourer);
        var addedGlamourerEntry = false;
        if (hasGlamourerData &&
            (lastGlamourerEntry == null || lastGlamourerEntry.GlamourerString != snapshotData.Glamourer))
        {
            var entryStamp = DateTime.UtcNow;
            var newEntry = GlamourerHistoryEntry.Create(snapshotData.Glamourer,
                $"Glamourer Update - {entryStamp:yyyy-MM-dd HH:mm:ss} UTC", snapshotInfo.CurrentFileMapId);
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
                $"Files Update - {entryStamp:yyyy-MM-dd HH:mm:ss} UTC", snapshotInfo.CurrentFileMapId);
            glamourerHistory.Entries.Add(newEntry);
            PluginLog.Debug("File map changed without Glamourer change. Added history entry to capture file map.");
        }

        var b64Customize = string.IsNullOrEmpty(snapshotData.Customize)
            ? ""
            : Convert.ToBase64String(Encoding.UTF8.GetBytes(snapshotData.Customize));
        var lastCustomizeEntry = customizeHistory.Entries.LastOrDefault();
        if ((lastCustomizeEntry == null || lastCustomizeEntry.CustomizeData != b64Customize) &&
            !string.IsNullOrEmpty(b64Customize))
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
