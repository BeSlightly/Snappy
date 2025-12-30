using System.Collections.Generic;
using Dalamud.Utility;
using ECommons.GameHelpers;
using Snappy.Common;
using Snappy.Common.Utilities;
using Penumbra.GameData.Structs;

namespace Snappy.Services.SnapshotManager;

public class SnapshotApplicationService : ISnapshotApplicationService
{
    private readonly IActiveSnapshotManager _activeSnapshotManager;
    private readonly IIpcManager _ipcManager;
    private const string EmptyCustomizePlusProfileJson = "{\"Bones\":{}}";

    public SnapshotApplicationService(IIpcManager ipcManager, IActiveSnapshotManager activeSnapshotManager)
    {
        _ipcManager = ipcManager;
        _activeSnapshotManager = activeSnapshotManager;
    }

    public bool LoadSnapshot(
        ICharacter characterApplyTo,
        int objIdx,
        string path,
        GlamourerHistoryEntry? glamourerOverride = null,
        CustomizeHistoryEntry? customizeOverride = null,
        SnapshotLoadComponents loadComponents = SnapshotLoadComponents.All
    )
    {
        // This method is called from the UI thread, but file I/O should be async.
        // It's not a Task because the caller is a simple button handler.
        // We will load the files async and then apply the changes.
        // For a full refactor, this would return a Task. For now, we use GetResultSafely()
        // to keep the method signature while still using async file reads.

        if (!characterApplyTo.IsValid())
        {
            Notify.Error("Invalid character selected for snapshot loading.");
            return false;
        }

        var localPlayer = Player.Object;
        var isOnLocalPlayer = (localPlayer != null && characterApplyTo.ObjectIndex == localPlayer.ObjectIndex) ||
                              characterApplyTo.ObjectIndex == ObjectIndex.GPosePlayer.Index;

        var paths = SnapshotPaths.From(path);

        var snapshotInfo = JsonUtil.DeserializeAsync<SnapshotInfo>(paths.SnapshotFile).GetResultSafely();
        if (snapshotInfo == null)
        {
            Notify.Error($"Could not load snapshot: {Constants.SnapshotFileName} not found or invalid in {path}");
            return false;
        }

        var glamourerHistory =
            JsonUtil.DeserializeAsync<GlamourerHistory>(paths.GlamourerHistoryFile).GetResultSafely() ??
            new GlamourerHistory();
        var customizeHistory =
            JsonUtil.DeserializeAsync<CustomizeHistory>(paths.CustomizeHistoryFile).GetResultSafely() ??
            new CustomizeHistory();

        var applyFiles = loadComponents.HasFlag(SnapshotLoadComponents.Files);
        var applyGlamourer = loadComponents.HasFlag(SnapshotLoadComponents.Glamourer);
        var applyCustomize = loadComponents.HasFlag(SnapshotLoadComponents.CustomizePlus);

        var glamourerToApply = applyGlamourer ? glamourerOverride ?? glamourerHistory.Entries.LastOrDefault() : null;
        string? customizeDataToApply = null;
        if (applyCustomize)
        {
            if (customizeOverride != null)
                customizeDataToApply = customizeOverride.CustomizeData;
            else if (applyGlamourer && glamourerToApply != null)
            {
                if (glamourerToApply.CustomizeData != null)
                    customizeDataToApply = glamourerToApply.CustomizeData;
                else
                    customizeDataToApply = FindClosestCustomizeEntry(customizeHistory.Entries, glamourerToApply)
                        ?.CustomizeData;
            }
            else
            {
                customizeDataToApply = customizeHistory.Entries.LastOrDefault()?.CustomizeData;
            }
        }

        var resolvedFileMap = new Dictionary<string, string>();
        if (applyFiles)
        {
            var mapIdToUse = glamourerToApply?.FileMapId ?? customizeOverride?.FileMapId ??
                             snapshotInfo.CurrentFileMapId;
            resolvedFileMap = FileMapUtil.ResolveFileMap(snapshotInfo, mapIdToUse);
        }

        var customizePlusAvailable = applyCustomize && _ipcManager.IsCustomizePlusAvailable();
        var hasAnyData = applyFiles
                         || (applyGlamourer && glamourerToApply != null &&
                             !string.IsNullOrEmpty(glamourerToApply.GlamourerString))
                         || customizePlusAvailable;
        if (!hasAnyData)
        {
            Notify.Error("Could not load snapshot: No data (files, glamour, C+) to apply.");
            return false;
        }

        var moddedPaths = new Dictionary<string, string>();
        if (applyFiles && resolvedFileMap.Any())
        {
            foreach (var (gamePath, hash) in resolvedFileMap)
            {
                var hashedFilePath = paths.ResolveHashedFilePath(hash, gamePath);

                if (File.Exists(hashedFilePath))
                    moddedPaths[gamePath] = hashedFilePath;
                else
                    PluginLog.Warning($"Missing file blob for {gamePath} (hash: {hash}). It will not be applied.");
            }
        }

        if (applyFiles)
        {
            _ipcManager.PenumbraRemoveTemporaryCollection(characterApplyTo.ObjectIndex);
            if (moddedPaths.Any() || !string.IsNullOrEmpty(snapshotInfo.ManipulationString))
                _ipcManager.PenumbraSetTempMods(characterApplyTo, objIdx, moddedPaths, snapshotInfo.ManipulationString);
        }

        var existingSnapshot = _activeSnapshotManager.GetSnapshotForCharacter(characterApplyTo);
        _activeSnapshotManager.RemoveAllSnapshotsForCharacter(characterApplyTo);
        Guid? cplusProfileId = null;
        if (customizePlusAvailable)
        {
            _ipcManager.ClearCustomizePlusTemporaryProfile(characterApplyTo.ObjectIndex);

            if (!string.IsNullOrEmpty(customizeDataToApply))
            {
                string cplusJson;
                try
                {
                    cplusJson = Encoding.UTF8.GetString(Convert.FromBase64String(customizeDataToApply));
                }
                catch
                {
                    cplusJson = customizeDataToApply;
                }

                cplusProfileId = _ipcManager.SetCustomizePlusScale(characterApplyTo.Address, cplusJson);
            }
            else if (isOnLocalPlayer)
            {
                // Apply a temporary empty profile to neutralize local C+ when the snapshot has no C+ data.
                cplusProfileId = _ipcManager.SetCustomizePlusScale(characterApplyTo.Address,
                    EmptyCustomizePlusProfileJson);
            }
        }

        var appliedGlamourerState = false;
        if (applyGlamourer && glamourerToApply != null && !string.IsNullOrEmpty(glamourerToApply.GlamourerString))
        {
            _ipcManager.ApplyGlamourerState(glamourerToApply.GlamourerString, characterApplyTo);
            appliedGlamourerState = true;
        }

        var shouldRedraw = applyFiles || appliedGlamourerState;
        if (shouldRedraw)
            _ipcManager.PenumbraRedraw(objIdx);

        var hasPenumbraCollection = applyFiles || existingSnapshot?.HasPenumbraCollection == true;
        var hasGlamourerState = appliedGlamourerState || existingSnapshot?.HasGlamourerState == true;
        var isGlamourerLocked = appliedGlamourerState
            ? true
            : existingSnapshot?.IsGlamourerLocked ?? false;
        var finalCplusProfileId = customizePlusAvailable
            ? cplusProfileId
            : existingSnapshot?.CustomizePlusProfileId;

        _activeSnapshotManager.AddSnapshot(new ActiveSnapshot(characterApplyTo.ObjectIndex, finalCplusProfileId,
            isOnLocalPlayer, characterApplyTo.Name.TextValue, isGlamourerLocked, hasPenumbraCollection,
            hasGlamourerState));
        PluginLog.Debug(
            $"Snapshot loaded for index {characterApplyTo.ObjectIndex}. 'IsOnLocalPlayer' flag set to: {isOnLocalPlayer}.");

        var snapshotName = Path.GetFileName(path);
        Notify.Success($"Loaded snapshot '{snapshotName}' onto {characterApplyTo.Name.TextValue}.");

        return true;
    }

    private static CustomizeHistoryEntry? FindClosestCustomizeEntry(IReadOnlyList<CustomizeHistoryEntry> entries,
        GlamourerHistoryEntry glamourerEntry)
    {
        if (entries.Count == 0)
            return null;

        if (!TryParseHistoryTimestamp(glamourerEntry.Timestamp, out var glamourerTimestamp))
            return entries.LastOrDefault();

        CustomizeHistoryEntry? closest = null;
        var closestDelta = TimeSpan.MaxValue;
        foreach (var entry in entries)
        {
            if (!TryParseHistoryTimestamp(entry.Timestamp, out var entryTimestamp))
                continue;

            var delta = (entryTimestamp - glamourerTimestamp).Duration();
            if (delta < closestDelta)
            {
                closestDelta = delta;
                closest = entry;
            }
        }

        return closest ?? entries.LastOrDefault();
    }

    private static bool TryParseHistoryTimestamp(string? timestamp, out DateTime parsedUtc)
    {
        return DateTime.TryParse(timestamp, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out parsedUtc);
    }
}
