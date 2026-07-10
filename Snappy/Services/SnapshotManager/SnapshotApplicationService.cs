using ECommons.GameHelpers;
using Snappy.Common;
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
        if (!characterApplyTo.IsValid())
        {
            Notify.Error("Invalid character selected for snapshot loading.");
            return false;
        }

        var localPlayer = Player.Object;
        var isOnLocalPlayer = (localPlayer != null && characterApplyTo.ObjectIndex == localPlayer.ObjectIndex) ||
                              characterApplyTo.ObjectIndex == ObjectIndex.GPosePlayer.Index;

        SnapshotLoadPlan plan;
        try
        {
            if (!TryBuildLoadPlan(path, glamourerOverride, customizeOverride, loadComponents, out plan,
                    out var errorMessage))
            {
                Notify.Error(errorMessage);
                return false;
            }
        }
        catch (Exception ex)
        {
            Notify.Error($"Could not load snapshot: {ex.Message}");
            PluginLog.Error($"Failed to build snapshot load plan for '{path}': {ex}");
            return false;
        }

        if (!ApplyLoadPlan(characterApplyTo, objIdx, isOnLocalPlayer, plan))
        {
            Notify.Error("Could not load snapshot: Penumbra rejected the temporary collection.");
            return false;
        }

        var snapshotName = Path.GetFileName(path);
        // Missing blobs are already omitted from the load plan and logged at Debug/Warning.
        // Do not toast about them — load success is enough (e.g. intentionally incomplete Mare captures).
        Notify.Success($"Loaded snapshot '{snapshotName}' onto {characterApplyTo.Name.TextValue}.");

        return true;
    }

    private bool ApplyLoadPlan(ICharacter characterApplyTo, int objIdx, bool isOnLocalPlayer, SnapshotLoadPlan plan)
    {
        var appliedPenumbraCollection = false;
        if (plan.ApplyFiles)
        {
            if (plan.ModdedPaths.Any() || !string.IsNullOrEmpty(plan.ResolvedManipulations))
            {
                appliedPenumbraCollection = _ipcManager.PenumbraSetTempMods(characterApplyTo, objIdx,
                    plan.ModdedPaths, plan.ResolvedManipulations);
                if (!appliedPenumbraCollection)
                    return false;
            }
            else if (!_ipcManager.PenumbraRemoveTemporaryCollection(characterApplyTo.ObjectIndex))
            {
                return false;
            }
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

                if (CustomizePlusUtil.TryNormalizeIpcProfileJson(cplusJson, out var normalizedProfileJson))
                    cplusJson = normalizedProfileJson;

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

        var hasPenumbraCollection = plan.ApplyFiles
            ? appliedPenumbraCollection
            : existingSnapshot?.HasPenumbraCollection == true;
        var hasGlamourerState = appliedGlamourerState || existingSnapshot?.HasGlamourerState == true;
        var isGlamourerLocked = appliedGlamourerState
            ? true
            : existingSnapshot?.IsGlamourerLocked ?? false;
        var finalCplusProfileId = plan.CustomizePlusAvailable
            ? cplusProfileId
            : existingSnapshot?.CustomizePlusProfileId;

        var appliedGlamourer = plan.AppliedGlamourerTimestamp != null;
        var appliedCustomizeTracked = plan.CustomizePlusAvailable && plan.AppliedCustomizeTimestamp != null;
        _activeSnapshotManager.AddSnapshot(new ActiveSnapshot(characterApplyTo.ObjectIndex, finalCplusProfileId,
            isOnLocalPlayer, characterApplyTo.Name.TextValue, isGlamourerLocked, hasPenumbraCollection,
            hasGlamourerState)
        {
            GlamourerSnapshotPath = appliedGlamourer
                ? plan.SnapshotPath
                : existingSnapshot?.GlamourerSnapshotPath,
            GlamourerHistoryTimestamp = appliedGlamourer
                ? plan.AppliedGlamourerTimestamp
                : existingSnapshot?.GlamourerHistoryTimestamp,
            CustomizeSnapshotPath = plan.CustomizePlusAvailable
                ? appliedCustomizeTracked ? plan.SnapshotPath : null
                : existingSnapshot?.CustomizeSnapshotPath,
            CustomizeHistoryTimestamp = plan.CustomizePlusAvailable
                ? plan.AppliedCustomizeTimestamp
                : existingSnapshot?.CustomizeHistoryTimestamp
        });
        PluginLog.Debug(
            $"Snapshot loaded for index {characterApplyTo.ObjectIndex}. 'IsOnLocalPlayer' flag set to: {isOnLocalPlayer}.");
        return true;
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
        var snapshotInfo = JsonUtil.DeserializeStateAsync<SnapshotInfo>(paths.SnapshotFile).GetAwaiter().GetResult();
        if (snapshotInfo == null)
        {
            errorMessage = $"Could not load snapshot: {Constants.SnapshotFileName} not found or invalid in {path}";
            return false;
        }

        var glamourerHistory =
            JsonUtil.DeserializeStateAsync<GlamourerHistory>(paths.GlamourerHistoryFile).GetAwaiter().GetResult() ??
            new GlamourerHistory();
        var customizeHistory =
            JsonUtil.DeserializeStateAsync<CustomizeHistory>(paths.CustomizeHistoryFile).GetAwaiter().GetResult() ??
            new CustomizeHistory();

        var applyFiles = loadComponents.HasFlag(SnapshotLoadComponents.Files);
        var applyGlamourer = loadComponents.HasFlag(SnapshotLoadComponents.Glamourer);
        var applyCustomize = loadComponents.HasFlag(SnapshotLoadComponents.CustomizePlus);

        var glamourerToApply = applyGlamourer ? glamourerOverride ?? glamourerHistory.Entries.LastOrDefault() : null;
        string? customizeDataToApply = null;
        CustomizeHistoryEntry? customizeEntryApplied = null;
        if (applyCustomize)
        {
            if (customizeOverride != null)
            {
                customizeEntryApplied = customizeOverride;
                customizeDataToApply = customizeOverride.CustomizeData;
            }
            else if (applyGlamourer && glamourerToApply != null)
            {
                if (glamourerToApply.CustomizeData != null)
                {
                    customizeDataToApply = glamourerToApply.CustomizeData;
                    customizeEntryApplied =
                        FindCustomizeEntryByData(customizeHistory.Entries, glamourerToApply.CustomizeData);
                }
                else
                {
                    customizeEntryApplied =
                        FindClosestCustomizeEntry(customizeHistory.Entries, glamourerToApply);
                    customizeDataToApply = customizeEntryApplied?.CustomizeData;
                }
            }
            else
            {
                customizeEntryApplied = customizeHistory.Entries.LastOrDefault();
                customizeDataToApply = customizeEntryApplied?.CustomizeData;
            }
        }

        var resolvedFileMap = new Dictionary<string, string>();
        var resolvedFileSwaps = new Dictionary<string, string>();
        var resolvedManipulations = string.Empty;
        if (applyFiles)
        {
            var mapIdToUse = glamourerToApply?.FileMapId ?? customizeOverride?.FileMapId ??
                             snapshotInfo.CurrentFileMapId;
            resolvedFileMap = FileMapUtil.ResolveFileMap(snapshotInfo, mapIdToUse);
            resolvedFileSwaps = FileMapUtil.ResolveFileSwaps(snapshotInfo, mapIdToUse);
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
        var missingFileCount = 0;
        if (applyFiles && resolvedFileMap.Any())
        {
            var missingGamePaths = new List<string>();
            foreach (var (gamePath, hash) in resolvedFileMap)
            {
                var hashedFilePath = paths.ResolveHashedFilePath(hash, gamePath);

                if (File.Exists(hashedFilePath))
                    moddedPaths[gamePath] = hashedFilePath;
                else
                    missingGamePaths.Add(gamePath);
            }

            if (missingGamePaths.Count > 0)
            {
                missingFileCount = missingGamePaths.Count;
                var loggedPaths = string.Join(", ", missingGamePaths.Take(10));
                var omittedCount = missingGamePaths.Count - Math.Min(missingGamePaths.Count, 10);
                PluginLog.Warning(
                    $"Snapshot '{path}' will be applied without these missing files: {loggedPaths}"
                    + (omittedCount > 0 ? $" (and {omittedCount} more)" : string.Empty));
            }
        }

        if (applyFiles)
            foreach (var (gamePath, swapPath) in resolvedFileSwaps)
                if (!string.IsNullOrWhiteSpace(gamePath) && !string.IsNullOrWhiteSpace(swapPath))
                    moddedPaths[gamePath] = swapPath;

        string? appliedGlamourerTimestamp = applyGlamourer && glamourerToApply != null
            ? glamourerToApply.Timestamp
            : null;
        string? appliedCustomizeTimestamp = applyCustomize ? customizeEntryApplied?.Timestamp : null;

        plan = new SnapshotLoadPlan(glamourerToApply, customizeDataToApply, moddedPaths, resolvedManipulations,
            applyFiles, applyGlamourer, customizePlusAvailable, missingFileCount, Path.GetFullPath(path),
            appliedGlamourerTimestamp, appliedCustomizeTimestamp);
        return true;
    }

    private sealed record SnapshotLoadPlan(
        GlamourerHistoryEntry? GlamourerToApply,
        string? CustomizeDataToApply,
        Dictionary<string, string> ModdedPaths,
        string ResolvedManipulations,
        bool ApplyFiles,
        bool ApplyGlamourer,
        bool CustomizePlusAvailable,
        int MissingFileCount,
        string SnapshotPath,
        string? AppliedGlamourerTimestamp,
        string? AppliedCustomizeTimestamp);

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

    private static CustomizeHistoryEntry? FindCustomizeEntryByData(IReadOnlyList<CustomizeHistoryEntry> entries,
        string customizeData)
    {
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            if (string.Equals(entries[i].CustomizeData, customizeData, StringComparison.Ordinal))
                return entries[i];
        }

        return null;
    }

}
