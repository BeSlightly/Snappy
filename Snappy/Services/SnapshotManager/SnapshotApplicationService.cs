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
        CustomizeHistoryEntry? customizeOverride = null
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

        var glamourerToApply = glamourerOverride ?? glamourerHistory.Entries.LastOrDefault();
        var customizeToApply = customizeOverride ?? customizeHistory.Entries.LastOrDefault();
        var mapIdToUse = glamourerToApply?.FileMapId ?? customizeToApply?.FileMapId ?? snapshotInfo.CurrentFileMapId;
        var resolvedFileMap = FileMapUtil.ResolveFileMap(snapshotInfo, mapIdToUse);
        if (!resolvedFileMap.Any() && snapshotInfo.FileReplacements.Any())
            resolvedFileMap = new Dictionary<string, string>(snapshotInfo.FileReplacements,
                StringComparer.OrdinalIgnoreCase);

        if (glamourerToApply == null && customizeToApply == null && !resolvedFileMap.Any())
        {
            Notify.Error("Could not load snapshot: No data (files, glamour, C+) to apply.");
            return false;
        }

        var moddedPaths = new Dictionary<string, string>();
        if (resolvedFileMap.Any())
        {
            foreach (var (gamePath, hash) in resolvedFileMap)
            {
                var hashedFilePath = paths.ResolveHashedFilePath(hash, gamePath);

                if (File.Exists(hashedFilePath))
                    moddedPaths[gamePath] = hashedFilePath;
                else
                    PluginLog.Warning($"Missing file blob for {gamePath} (hash: {hash}). It will not be applied.");
            }

            _ipcManager.PenumbraRemoveTemporaryCollection(characterApplyTo.ObjectIndex);
            _ipcManager.PenumbraSetTempMods(characterApplyTo, objIdx, moddedPaths, snapshotInfo.ManipulationString);
        }

        _activeSnapshotManager.RemoveAllSnapshotsForCharacter(characterApplyTo);
        Guid? cplusProfileId = null;

        if (_ipcManager.IsCustomizePlusAvailable() && customizeToApply != null &&
            !string.IsNullOrEmpty(customizeToApply.CustomizeData))
        {
            string cplusJson;
            try
            {
                cplusJson = Encoding.UTF8.GetString(Convert.FromBase64String(customizeToApply.CustomizeData));
            }
            catch
            {
                cplusJson = customizeToApply.CustomizeData;
            }

            cplusProfileId = _ipcManager.SetCustomizePlusScale(characterApplyTo.Address, cplusJson);
        }

        var isGlamourerLocked = false;
        if (glamourerToApply != null)
        {
            _ipcManager.ApplyGlamourerState(glamourerToApply.GlamourerString, characterApplyTo);
            isGlamourerLocked = true; // Glamourer is locked when we apply a state
        }

        _ipcManager.PenumbraRedraw(objIdx);

        var isOnLocalPlayer = (Player.Available && characterApplyTo.ObjectIndex == Player.Object.ObjectIndex) ||
                              characterApplyTo.ObjectIndex == ObjectIndex.GPosePlayer.Index;

        _activeSnapshotManager.AddSnapshot(new ActiveSnapshot(characterApplyTo.ObjectIndex, cplusProfileId,
            isOnLocalPlayer, characterApplyTo.Name.TextValue, isGlamourerLocked));
        PluginLog.Debug(
            $"Snapshot loaded for index {characterApplyTo.ObjectIndex}. 'IsOnLocalPlayer' flag set to: {isOnLocalPlayer}.");

        var snapshotName = Path.GetFileName(path);
        Notify.Success($"Loaded snapshot '{snapshotName}' onto {characterApplyTo.Name.TextValue}.");

        return true;
    }
}
