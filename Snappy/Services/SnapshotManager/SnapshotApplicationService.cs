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

        if (!TryBuildLoadPlan(path, glamourerOverride, customizeOverride, loadComponents, out var plan,
                out var errorMessage))
        {
            Notify.Error(errorMessage);
            return false;
        }

        ApplyLoadPlan(characterApplyTo, objIdx, isOnLocalPlayer, plan);

        var snapshotName = Path.GetFileName(path);
        Notify.Success($"Loaded snapshot '{snapshotName}' onto {characterApplyTo.Name.TextValue}.");

        return true;
    }

    private void ApplyLoadPlan(ICharacter characterApplyTo, int objIdx, bool isOnLocalPlayer, SnapshotLoadPlan plan)
    {
        if (plan.ApplyFiles)
        {
            _ipcManager.PenumbraRemoveTemporaryCollection(characterApplyTo.ObjectIndex);
            if (plan.ModdedPaths.Any() || !string.IsNullOrEmpty(plan.ResolvedManipulations))
                _ipcManager.PenumbraSetTempMods(characterApplyTo, objIdx, plan.ModdedPaths, plan.ResolvedManipulations);
        }

        var existingSnapshot = _activeSnapshotManager.GetSnapshotForCharacter(characterApplyTo);
        _activeSnapshotManager.RemoveAllSnapshotsForCharacter(characterApplyTo);
        Guid? cplusProfileId = null;
        if (plan.CustomizePlusAvailable)
        {
            _ipcManager.ClearCustomizePlusTemporaryProfile(characterApplyTo.ObjectIndex);

            if (!string.IsNullOrEmpty(plan.CustomizeDataToApply))
            {
                string cplusJson;
                try
                {
                    cplusJson = Encoding.UTF8.GetString(Convert.FromBase64String(plan.CustomizeDataToApply));
                }
                catch
                {
                    cplusJson = plan.CustomizeDataToApply;
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
        if (plan.ApplyGlamourer && plan.GlamourerToApply != null
                                && !string.IsNullOrEmpty(plan.GlamourerToApply.GlamourerString))
        {
            _ipcManager.ApplyGlamourerState(plan.GlamourerToApply.GlamourerString, characterApplyTo);
            appliedGlamourerState = true;
        }

        var shouldRedraw = plan.ApplyFiles || appliedGlamourerState;
        if (shouldRedraw)
            _ipcManager.PenumbraRedraw(objIdx);

        var hasPenumbraCollection = plan.ApplyFiles || existingSnapshot?.HasPenumbraCollection == true;
        var hasGlamourerState = appliedGlamourerState || existingSnapshot?.HasGlamourerState == true;
        var isGlamourerLocked = appliedGlamourerState
            ? true
            : existingSnapshot?.IsGlamourerLocked ?? false;
        var finalCplusProfileId = plan.CustomizePlusAvailable
            ? cplusProfileId
            : existingSnapshot?.CustomizePlusProfileId;

        _activeSnapshotManager.AddSnapshot(new ActiveSnapshot(characterApplyTo.ObjectIndex, finalCplusProfileId,
            isOnLocalPlayer, characterApplyTo.Name.TextValue, isGlamourerLocked, hasPenumbraCollection,
            hasGlamourerState));
        PluginLog.Debug(
            $"Snapshot loaded for index {characterApplyTo.ObjectIndex}. 'IsOnLocalPlayer' flag set to: {isOnLocalPlayer}.");
    }

    private bool TryBuildLoadPlan(
        string path,
        GlamourerHistoryEntry? glamourerOverride,
        CustomizeHistoryEntry? customizeOverride,
        SnapshotLoadComponents loadComponents,
        out SnapshotLoadPlan plan,
        out string errorMessage)
    {
        plan = null!;
        errorMessage = string.Empty;

        var paths = SnapshotPaths.From(path);
        var snapshotInfo = JsonUtil.DeserializeAsync<SnapshotInfo>(paths.SnapshotFile).GetResultSafely();
        if (snapshotInfo == null)
        {
            errorMessage = $"Could not load snapshot: {Constants.SnapshotFileName} not found or invalid in {path}";
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
        var resolvedManipulations = string.Empty;
        if (applyFiles)
        {
            var mapIdToUse = glamourerToApply?.FileMapId ?? customizeOverride?.FileMapId ??
                             snapshotInfo.CurrentFileMapId;
            resolvedFileMap = FileMapUtil.ResolveFileMap(snapshotInfo, mapIdToUse);
            resolvedManipulations = FileMapUtil.ResolveManipulation(snapshotInfo, mapIdToUse);
        }

        var customizePlusAvailable = applyCustomize && _ipcManager.IsCustomizePlusAvailable();
        var hasAnyData = applyFiles
                         || (applyGlamourer && glamourerToApply != null &&
                             !string.IsNullOrEmpty(glamourerToApply.GlamourerString))
                         || customizePlusAvailable;
        if (!hasAnyData)
        {
            errorMessage = "Could not load snapshot: No data (files, glamour, C+) to apply.";
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

        plan = new SnapshotLoadPlan(glamourerToApply, customizeDataToApply, moddedPaths, resolvedManipulations,
            applyFiles, applyGlamourer, customizePlusAvailable);
        return true;
    }

    private sealed record SnapshotLoadPlan(
        GlamourerHistoryEntry? GlamourerToApply,
        string? CustomizeDataToApply,
        Dictionary<string, string> ModdedPaths,
        string ResolvedManipulations,
        bool ApplyFiles,
        bool ApplyGlamourer,
        bool CustomizePlusAvailable);

    private static CustomizeHistoryEntry? FindClosestCustomizeEntry(IReadOnlyList<CustomizeHistoryEntry> entries,
        GlamourerHistoryEntry glamourerEntry)
    {
        if (entries.Count == 0)
            return null;

        if (!HistoryEntryUtil.TryParseTimestamp(glamourerEntry.Timestamp, out var glamourerTimestamp))
            return entries.LastOrDefault();

        CustomizeHistoryEntry? closest = null;
        var closestDelta = TimeSpan.MaxValue;
        foreach (var entry in entries)
        {
            if (!HistoryEntryUtil.TryParseTimestamp(entry.Timestamp, out var entryTimestamp))
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

}
