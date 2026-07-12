using ECommons.ExcelServices;
using Luna;
using Snappy.Common;
using System.Threading;

namespace Snappy.Services.SnapshotManager;

public class SnapshotFileService : ISnapshotFileService
{
    private readonly Configuration _configuration;
    private readonly IIpcManager _ipcManager;
    private readonly PenumbraCollectionSnapshotDataBuilder _collectionSnapshotDataBuilder;
    private readonly LiveSnapshotDataBuilder _liveSnapshotDataBuilder;
    private readonly MareSnapshotDataBuilder _mareSnapshotDataBuilder;
    private readonly ISnapshotIndexService _snapshotIndexService;
    private readonly SemaphoreSlim _snapshotMutationGate = new(1, 1);

    public SnapshotFileService(Configuration configuration, IIpcManager ipcManager,
        ISnapshotIndexService snapshotIndexService)
    {
        _configuration = configuration;
        _ipcManager = ipcManager;
        _collectionSnapshotDataBuilder = new PenumbraCollectionSnapshotDataBuilder();
        _liveSnapshotDataBuilder = new LiveSnapshotDataBuilder();
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

    public async Task<string?> UpdateSnapshotAsync(ICharacter character, bool isLocalPlayer)
    {
        var capture = await Svc.Framework.RunOnFrameworkThread(() =>
            CaptureSnapshotState(character, isLocalPlayer)).ConfigureAwait(false);
        if (capture == null)
            return null;

        await _snapshotMutationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(() => UpdateSnapshotFromCaptureAsync(capture)).ConfigureAwait(false);
        }
        finally
        {
            _snapshotMutationGate.Release();
        }
    }

    private SnapshotCapture? CaptureSnapshotState(ICharacter character, bool isLocalPlayer)
    {
        if (!character.IsValid())
        {
            Notify.Error("Invalid character selected for snapshot update.");
            return null;
        }

        var charaName = character.Name.TextValue;
        var objectIndex = character.ObjectIndex;
        var (resolvedWorldId, resolvedWorldName) = ResolveHomeWorld(character, charaName);
        var snapshotPath = _snapshotIndexService.FindSnapshotPathForActor(character) ??
                           BuildDefaultSnapshotPath(_configuration.WorkingDirectory, charaName, resolvedWorldId,
                               resolvedWorldName);
        var useLiveData = _configuration.UseLiveSnapshotData || isLocalPlayer;

        SnapshotLiveState? liveState = null;
        SnapshotData? mareData = null;
        if (useLiveData)
        {
            liveState = new SnapshotLiveState(charaName, _ipcManager.GetGlamourerState(character),
                _ipcManager.GetCustomizePlusScale(character), _ipcManager.GetMetaManipulations(objectIndex));
        }
        else
        {
            mareData = _mareSnapshotDataBuilder.BuildFromMare(character);
        }

        var penumbraPaths = CapturePenumbraPaths(objectIndex, useLiveData);

        return new SnapshotCapture(charaName, objectIndex, resolvedWorldId, resolvedWorldName, snapshotPath,
            useLiveData, !_configuration.UseLiveSnapshotData && !isLocalPlayer, liveState, mareData, penumbraPaths);
    }

    private PenumbraPathState CapturePenumbraPaths(int objectIndex, bool useLiveData)
    {
        var resourcePaths = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var collectionFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (useLiveData && _configuration.UsePenumbraIpcResourcePaths)
        {
            resourcePaths = _ipcManager.PenumbraGetGameObjectResourcePaths(objectIndex);
        }
        else
        {
            collectionFiles = _ipcManager.PenumbraGetCollectionResolvedFiles(objectIndex);
            if (!useLiveData && collectionFiles.Count == 0)
                resourcePaths = _ipcManager.PenumbraGetGameObjectResourcePaths(objectIndex);
        }

        return new PenumbraPathState(resourcePaths, collectionFiles);
    }

    private async Task<string?> UpdateSnapshotFromCaptureAsync(SnapshotCapture capture)
    {
        var now = DateTime.UtcNow;

        var snapshotData = BuildSnapshotData(capture);

        if (snapshotData == null) return null;

        if (!capture.UseLiveData)
        {
            BackfillSnapshotDataFromPenumbra(snapshotData, capture.PenumbraPaths);
        }

        snapshotData = NormalizeSnapshotData(snapshotData);

        var paths = SnapshotPaths.From(capture.SnapshotPath);
        Directory.CreateDirectory(paths.RootPath);
        Directory.CreateDirectory(paths.FilesDirectory);

        var isNewSnapshot = !File.Exists(paths.SnapshotFile);

        var snapshotInfo = await JsonUtil.DeserializeStateAsync<SnapshotInfo>(paths.SnapshotFile)
                           ?? new SnapshotInfo { SourceActor = capture.CharacterName };
        snapshotInfo.FileMaps ??= new List<FileMapEntry>();
        snapshotInfo.FileSwaps ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Backfill missing per-map manipulation data for older snapshots.
        BackfillManipulationStrings(snapshotInfo);
        UpdateSourceWorld(snapshotInfo, capture.WorldId, capture.WorldName);

        var glamourerHistory = await JsonUtil.DeserializeStateAsync<GlamourerHistory>(paths.GlamourerHistoryFile) ??
                               new GlamourerHistory();
        var customizeHistory = await JsonUtil.DeserializeStateAsync<CustomizeHistory>(paths.CustomizeHistoryFile) ??
                               new CustomizeHistory();

        var resolvedCurrentMap =
            EnsureBaseFileMap(snapshotInfo, glamourerHistory, customizeHistory, now);
        var resolvedCurrentFileSwaps =
            FileMapUtil.ResolveFileSwaps(snapshotInfo, snapshotInfo.CurrentFileMapId);

        var includeRemovals = !capture.UseLiveData || !_configuration.UsePenumbraIpcResourcePaths;

        // Mare users often pause sounds/animations/emotes; those hashes stay in character data
        // but are absent from the local cache. Skip them instead of aborting the whole capture.
        var skippedMissingFiles = new List<string>();
        foreach (var (gamePath, hash) in snapshotData.FileReplacements.ToList())
        {
            var existingFilePath = paths.FindAnyExistingHashedFilePath(hash);
            var hashedFilePath = existingFilePath ?? paths.GetPreferredHashedFilePath(hash, gamePath);
            if (File.Exists(hashedFilePath))
                continue;

            string? sourceFile = null;
            if (capture.UseMareFileCache)
                sourceFile = _ipcManager.GetMareFileCachePath(hash);
            if (string.IsNullOrEmpty(sourceFile))
                snapshotData.ResolvedPaths.TryGetValue(hash, out sourceFile);

            if (!string.IsNullOrEmpty(sourceFile) && File.Exists(sourceFile))
            {
                File.Copy(sourceFile, hashedFilePath, true);
                continue;
            }

            snapshotData.FileReplacements.Remove(gamePath);
            skippedMissingFiles.Add(gamePath);
            PluginLog.Warning(
                $"Skipping missing source file for '{gamePath}' (hash: {hash}); not present in Mare cache or Penumbra backfill.");
        }

        if (skippedMissingFiles.Count > 0)
        {
            PluginLog.Warning(
                $"[Snappy] Skipped {skippedMissingFiles.Count} missing file(s) while capturing '{capture.CharacterName}'.");
        }

        var mapChanged = UpdateFileMaps(snapshotInfo, snapshotData, resolvedCurrentMap, resolvedCurrentFileSwaps,
            includeRemovals, now);

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

        if (!SaveSnapshotToDiskCore(capture.SnapshotPath, snapshotInfo, glamourerHistory, customizeHistory))
            throw new IOException($"Failed to save all snapshot state files in '{capture.SnapshotPath}'.");

        if (isNewSnapshot)
            Notify.Success($"New snapshot for '{capture.CharacterName}' created successfully.");
        else
            Notify.Success($"Snapshot for '{capture.CharacterName}' updated successfully.");

        if (skippedMissingFiles.Count > 0)
        {
            var example = skippedMissingFiles[0];
            var more = skippedMissingFiles.Count > 1
                ? $" (+{skippedMissingFiles.Count - 1} more)"
                : string.Empty;
            Notify.Warning(
                $"Skipped {skippedMissingFiles.Count} missing file(s) (paused Mare downloads / not cached), e.g. '{example}'{more}. Snapshot still saved.");
        }

        return capture.SnapshotPath;
    }

    private SnapshotData? BuildSnapshotData(SnapshotCapture capture)
    {
        if (capture.UseLiveData)
        {
            if (capture.LiveState == null)
                return null;

            // Penumbra maps and actor state are frozen together; file enumeration and hashing remain on the worker.
            return _configuration.UsePenumbraIpcResourcePaths
                ? _liveSnapshotDataBuilder.Build(capture.LiveState, capture.PenumbraPaths.ResourcePaths)
                : _collectionSnapshotDataBuilder.Build(capture.LiveState, capture.PenumbraPaths.CollectionFiles);
        }

        return capture.MareData;
    }

    private static SnapshotData NormalizeSnapshotData(SnapshotData snapshotData)
    {
        if (snapshotData.FileReplacements.Values.Any(hash => !SnapshotBlobUtil.TryNormalizeBlobId(hash, out _)))
            throw new InvalidDataException("Snapshot data contains an invalid file hash.");

        var fileReplacements = GamePathUtil.NormalizeFileMap(snapshotData.FileReplacements);
        var fileSwaps = GamePathUtil.NormalizeFileSwaps(snapshotData.FileSwaps);
        foreach (var gamePath in fileSwaps.Keys)
            fileReplacements.Remove(gamePath);

        return snapshotData with
        {
            FileReplacements = fileReplacements,
            FileSwaps = fileSwaps
        };
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
        var resolvedCurrentFileSwaps =
            FileMapUtil.ResolveFileSwaps(snapshotInfo, snapshotInfo.CurrentFileMapId);

        FileMapUtil.CreateBaseMapIfMissing(snapshotInfo, resolvedCurrentMap, resolvedCurrentFileSwaps, now);
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
        Dictionary<string, string> resolvedCurrentMap, Dictionary<string, string> resolvedCurrentFileSwaps,
        bool includeRemovals, DateTime now)
    {
        var incomingFileMap = new Dictionary<string, string>(snapshotData.FileReplacements,
            StringComparer.OrdinalIgnoreCase);
        var incomingFileSwaps = new Dictionary<string, string>(snapshotData.FileSwaps,
            StringComparer.OrdinalIgnoreCase);
        foreach (var gamePath in incomingFileSwaps.Keys)
            incomingFileMap.Remove(gamePath);
        var mapChanges = FileMapUtil.CalculateChanges(resolvedCurrentMap, incomingFileMap, includeRemovals);
        var fileSwapChanges = FileMapUtil.CalculateChanges(resolvedCurrentFileSwaps, incomingFileSwaps,
            includeRemovals);
        var fileMapChanged = mapChanges.Any() || fileSwapChanges.Any();

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
                FileSwapChanges = fileSwapChanges,
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
        snapshotInfo.FileSwaps =
            FileMapUtil.ResolveFileSwaps(snapshotInfo, snapshotInfo.CurrentFileMapId);

        return mapChanged;
    }

    private static void BackfillSnapshotDataFromPenumbra(SnapshotData snapshotData, PenumbraPathState penumbraPaths)
    {
        if (snapshotData.FileReplacements.Count == 0)
            return;

        var resolvedByGamePath = penumbraPaths.CollectionFiles;
        if (resolvedByGamePath.Count == 0)
        {
            if (penumbraPaths.ResourcePaths.Count > 0)
            {
                resolvedByGamePath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (resolvedPath, gamePaths) in penumbraPaths.ResourcePaths)
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
        int skippedVerify = 0;

        foreach (var (gamePath, expectedHash) in snapshotData.FileReplacements.ToList())
        {
            if (snapshotData.ResolvedPaths.ContainsKey(expectedHash))
                continue;

            if (!resolvedByGamePath.TryGetValue(gamePath, out var filePath))
                continue;

            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                continue;

            var isSha1 = expectedHash.Length == 40;
            var resolvedHash = expectedHash;
            if (isSha1)
            {
                if (!hashCache.TryGetValue(filePath, out var actualHash))
                {
                    actualHash = PluginUtil.GetFileHash(filePath);
                    if (string.IsNullOrEmpty(actualHash))
                        continue;
                    hashCache[filePath] = actualHash;
                }

                if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                    mismatched++;

                resolvedHash = actualHash;
                snapshotData.FileReplacements[gamePath] = actualHash;
            }
            else
            {
                skippedVerify++;
                snapshotData.FileReplacements[gamePath] = expectedHash;
            }

            if (!snapshotData.ResolvedPaths.ContainsKey(resolvedHash))
                snapshotData.ResolvedPaths[resolvedHash] = filePath;

            backfilled++;
        }

        if (backfilled > 0)
        {
            var skippedMessage = skippedVerify > 0 ? $", hash verification skipped: {skippedVerify}" : "";
            PluginLog.Debug(
                $"[Snappy] Backfilled {backfilled} file(s) from Penumbra for snapshot; hash mismatches: {mismatched}{skippedMessage}.");
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
            var sanitizedName = PathSanitizer.SanitizeFileSystemName(newName, string.Empty);
            if (!string.Equals(sanitizedName, newName, StringComparison.Ordinal))
            {
                Notify.Error("Snapshot names cannot contain path separators or invalid filename characters.");
                return;
            }

            var newPath = Path.CombineSafely(parent, sanitizedName);
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

    public async Task<HistoryEntryDeletionResult> DeleteHistoryEntryAsync(string snapshotPath,
        HistoryEntryBase entry, bool deleteUniqueGlamourerFiles)
    {
        await _snapshotMutationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await DeleteHistoryEntryCoreAsync(snapshotPath, entry, deleteUniqueGlamourerFiles)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            PluginLog.Error($"Failed to delete history entry from '{snapshotPath}': {ex}");
            return new HistoryEntryDeletionResult(false, ErrorMessage: ex.Message);
        }
        finally
        {
            _snapshotMutationGate.Release();
        }
    }

    private static async Task<HistoryEntryDeletionResult> DeleteHistoryEntryCoreAsync(string snapshotPath,
        HistoryEntryBase entry, bool deleteUniqueGlamourerFiles)
    {
        var paths = SnapshotPaths.From(snapshotPath);
        var snapshotInfo = await JsonUtil.DeserializeStateAsync<SnapshotInfo>(paths.SnapshotFile)
                           ?? throw new InvalidDataException("Snapshot state is missing.");
        var glamourerHistory = await JsonUtil.DeserializeStateAsync<GlamourerHistory>(paths.GlamourerHistoryFile)
                               ?? new GlamourerHistory();
        var customizeHistory = await JsonUtil.DeserializeStateAsync<CustomizeHistory>(paths.CustomizeHistoryFile)
                               ?? new CustomizeHistory();
        NormalizeDeletionState(snapshotInfo, glamourerHistory, customizeHistory);

        GlamourerHistoryEntry? deletedGlamourerEntry = null;
        if (entry is GlamourerHistoryEntry glamourerEntry)
        {
            var index = FindHistoryEntryIndex(glamourerHistory.Entries, glamourerEntry);
            if (index < 0)
                return new HistoryEntryDeletionResult(false, ErrorMessage: "The Glamourer history entry no longer exists.");

            deletedGlamourerEntry = glamourerHistory.Entries[index];
            glamourerHistory.Entries.RemoveAt(index);
        }
        else if (entry is CustomizeHistoryEntry customizeEntry)
        {
            var index = FindHistoryEntryIndex(customizeHistory.Entries, customizeEntry);
            if (index < 0)
                return new HistoryEntryDeletionResult(false, ErrorMessage: "The Customize+ history entry no longer exists.");

            customizeHistory.Entries.RemoveAt(index);
        }
        else
        {
            return new HistoryEntryDeletionResult(false, ErrorMessage: "Unsupported history entry type.");
        }

        var updatedSnapshotInfo = snapshotInfo;
        HashSet<string> uniqueBlobIds = [];
        string? cleanupSkippedReason = null;
        if (deleteUniqueGlamourerFiles && deletedGlamourerEntry != null)
        {
            if (!TryBuildUniqueBlobDeletionPlan(snapshotInfo, deletedGlamourerEntry, glamourerHistory,
                    customizeHistory, out updatedSnapshotInfo, out uniqueBlobIds, out cleanupSkippedReason))
            {
                updatedSnapshotInfo = snapshotInfo;
                uniqueBlobIds.Clear();
            }
        }

        if (!SaveSnapshotToDiskCore(snapshotPath, updatedSnapshotInfo, glamourerHistory, customizeHistory))
            return new HistoryEntryDeletionResult(false, ErrorMessage: "Could not save the updated snapshot state.");

        if (uniqueBlobIds.Count == 0)
            return new HistoryEntryDeletionResult(true, CleanupSkippedReason: cleanupSkippedReason);

        // Re-read the committed state and prove each candidate is still unreferenced before touching the file store.
        var committedInfo = await JsonUtil.DeserializeStateAsync<SnapshotInfo>(paths.SnapshotFile);
        var committedGlamourer = await JsonUtil.DeserializeStateAsync<GlamourerHistory>(paths.GlamourerHistoryFile)
                                 ?? new GlamourerHistory();
        var committedCustomize = await JsonUtil.DeserializeStateAsync<CustomizeHistory>(paths.CustomizeHistoryFile)
                                 ?? new CustomizeHistory();
        if (committedInfo != null)
            NormalizeDeletionState(committedInfo, committedGlamourer, committedCustomize);
        string? validationError = null;
        if (committedInfo == null ||
            !TryCollectReferencedBlobIds(committedInfo, committedGlamourer, committedCustomize,
                out var committedReferences, out validationError))
        {
            var reason = committedInfo == null ? "the committed snapshot state could not be reloaded" : validationError;
            PluginLog.Warning($"Unique file cleanup skipped for '{snapshotPath}': {reason}.");
            return new HistoryEntryDeletionResult(true, CleanupSkippedReason: reason);
        }

        uniqueBlobIds.ExceptWith(committedReferences);
        var deletedFileCount = 0;
        var failedFileCount = 0;
        foreach (var blobId in uniqueBlobIds)
        {
            IReadOnlyList<string> blobPaths;
            try
            {
                blobPaths = SnapshotBlobUtil.FindAllExistingBlobPaths(paths.FilesDirectory, blobId);
            }
            catch (Exception ex)
            {
                failedFileCount++;
                PluginLog.Error($"Could not enumerate snapshot blob '{blobId}' for deletion: {ex}");
                continue;
            }

            foreach (var blobPath in blobPaths)
            {
                try
                {
                    File.Delete(blobPath);
                    deletedFileCount++;
                }
                catch (Exception ex)
                {
                    failedFileCount++;
                    PluginLog.Error($"Could not delete unique snapshot file '{blobPath}': {ex}");
                }
            }
        }

        return new HistoryEntryDeletionResult(true, deletedFileCount, failedFileCount,
            CleanupSkippedReason: cleanupSkippedReason);
    }

    private static void NormalizeDeletionState(SnapshotInfo snapshotInfo, GlamourerHistory glamourerHistory,
        CustomizeHistory customizeHistory)
    {
        snapshotInfo.FileMaps ??= new List<FileMapEntry>();
        snapshotInfo.FileReplacements ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        snapshotInfo.FileSwaps ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        glamourerHistory.Entries ??= new List<GlamourerHistoryEntry>();
        customizeHistory.Entries ??= new List<CustomizeHistoryEntry>();
    }

    private static int FindHistoryEntryIndex<T>(IReadOnlyList<T> entries, T requestedEntry)
        where T : HistoryEntryBase
    {
        for (var i = 0; i < entries.Count; i++)
        {
            var candidate = entries[i];
            if (EqualityComparer<T>.Default.Equals(candidate, requestedEntry))
                return i;
        }

        return -1;
    }

    private static bool TryBuildUniqueBlobDeletionPlan(SnapshotInfo snapshotInfo,
        GlamourerHistoryEntry deletedEntry, GlamourerHistory remainingGlamourer,
        CustomizeHistory remainingCustomize, out SnapshotInfo updatedSnapshotInfo,
        out HashSet<string> uniqueBlobIds, out string? error)
    {
        updatedSnapshotInfo = snapshotInfo;
        uniqueBlobIds = [];
        error = null;

        if (!TryResolveHistoryFileMap(snapshotInfo, deletedEntry, out var deletedMap, out error))
            return false;

        var targetMapId = deletedEntry.FileMapId ?? snapshotInfo.CurrentFileMapId;
        var currentMapBelongsOnlyToDeletedEntry = !string.IsNullOrEmpty(targetMapId)
                                                  && string.Equals(snapshotInfo.CurrentFileMapId, targetMapId,
                                                      StringComparison.OrdinalIgnoreCase)
                                                  && !remainingGlamourer.Entries.Cast<HistoryEntryBase>()
                                                      .Concat(remainingCustomize.Entries)
                                                      .Any(remaining => HistoryEntryUsesMap(remaining, targetMapId,
                                                          snapshotInfo.CurrentFileMapId));

        if (currentMapBelongsOnlyToDeletedEntry)
        {
            var replacement = remainingGlamourer.Entries.LastOrDefault(e => !string.IsNullOrEmpty(e.FileMapId))
                              ?? (HistoryEntryBase?)remainingCustomize.Entries.LastOrDefault(e =>
                                  !string.IsNullOrEmpty(e.FileMapId));
            if (replacement == null)
            {
                updatedSnapshotInfo = snapshotInfo with
                {
                    CurrentFileMapId = null,
                    FileReplacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    FileSwaps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    ManipulationString = string.Empty
                };
            }
            else
            {
                if (!TryResolveHistoryFileMap(snapshotInfo, replacement, out var replacementMap, out error))
                    return false;

                var replacementMapId = replacement.FileMapId;
                try
                {
                    updatedSnapshotInfo = snapshotInfo with
                    {
                        CurrentFileMapId = replacementMapId,
                        FileReplacements = new Dictionary<string, string>(replacementMap,
                            StringComparer.OrdinalIgnoreCase),
                        FileSwaps = FileMapUtil.ResolveFileSwaps(snapshotInfo, replacementMapId),
                        ManipulationString = FileMapUtil.ResolveManipulation(snapshotInfo, replacementMapId)
                    };
                }
                catch (Exception ex)
                {
                    error = $"replacement file-map validation failed ({ex.Message}); no files were deleted";
                    updatedSnapshotInfo = snapshotInfo;
                    return false;
                }
            }
        }

        if (!TryCollectReferencedBlobIds(updatedSnapshotInfo, remainingGlamourer, remainingCustomize,
                out var remainingReferences, out error))
            return false;

        uniqueBlobIds = deletedMap.Values
            .Select(value => SnapshotBlobUtil.TryNormalizeBlobId(value, out var blobId) ? blobId : string.Empty)
            .Where(value => !string.IsNullOrEmpty(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        uniqueBlobIds.ExceptWith(remainingReferences);
        return true;
    }

    private static bool TryCollectReferencedBlobIds(SnapshotInfo snapshotInfo, GlamourerHistory glamourerHistory,
        CustomizeHistory customizeHistory, out HashSet<string> referencedBlobIds, out string? error)
    {
        referencedBlobIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        error = null;

        foreach (var entry in glamourerHistory.Entries.Cast<HistoryEntryBase>().Concat(customizeHistory.Entries))
        {
            if (!TryResolveHistoryFileMap(snapshotInfo, entry, out var fileMap, out error))
                return false;
            AddBlobIds(fileMap, referencedBlobIds);
        }

        if (!string.IsNullOrEmpty(snapshotInfo.CurrentFileMapId) || snapshotInfo.FileReplacements.Count > 0)
        {
            if (!TryResolveFileMapStrict(snapshotInfo, snapshotInfo.CurrentFileMapId, out var currentMap, out error))
                return false;
            AddBlobIds(currentMap, referencedBlobIds);
        }

        return true;
    }

    private static bool TryResolveHistoryFileMap(SnapshotInfo snapshotInfo, HistoryEntryBase entry,
        out Dictionary<string, string> fileMap, out string? error)
        => TryResolveFileMapStrict(snapshotInfo, entry.FileMapId ?? snapshotInfo.CurrentFileMapId, out fileMap,
            out error);

    private static bool TryResolveFileMapStrict(SnapshotInfo snapshotInfo, string? fileMapId,
        out Dictionary<string, string> fileMap, out string? error)
    {
        fileMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        error = null;
        try
        {
            if (!string.IsNullOrEmpty(fileMapId))
            {
                if (!FileMapUtil.TryResolveFileMap(snapshotInfo, fileMapId, out fileMap))
                {
                    error = $"file map '{fileMapId}' could not be resolved; no files were deleted";
                    return false;
                }

                return true;
            }

            fileMap = FileMapUtil.ResolveFileMap(snapshotInfo, null);
            return true;
        }
        catch (Exception ex)
        {
            error = $"file-map validation failed ({ex.Message}); no files were deleted";
            return false;
        }
    }

    private static bool HistoryEntryUsesMap(HistoryEntryBase entry, string mapId, string? currentMapId)
        => string.Equals(entry.FileMapId ?? currentMapId, mapId, StringComparison.OrdinalIgnoreCase);

    private static void AddBlobIds(IReadOnlyDictionary<string, string> fileMap, ISet<string> blobIds)
    {
        foreach (var hash in fileMap.Values)
            if (SnapshotBlobUtil.TryNormalizeBlobId(hash, out var blobId))
                blobIds.Add(blobId);
    }

    public void SaveSnapshotToDisk(string snapshotPath, SnapshotInfo info, GlamourerHistory glamourerHistory,
        CustomizeHistory customizeHistory)
    {
        _snapshotMutationGate.Wait();
        try
        {
            if (!SaveSnapshotToDiskCore(snapshotPath, info, glamourerHistory, customizeHistory))
                throw new IOException($"Failed to save all snapshot state files in '{snapshotPath}'.");
        }
        finally
        {
            _snapshotMutationGate.Release();
        }
    }

    private static bool SaveSnapshotToDiskCore(string snapshotPath, SnapshotInfo info,
        GlamourerHistory glamourerHistory, CustomizeHistory customizeHistory)
    {
        var paths = SnapshotPaths.From(snapshotPath);
        return JsonUtil.SerializeAll(
            (glamourerHistory, paths.GlamourerHistoryFile),
            (customizeHistory, paths.CustomizeHistoryFile),
            (info, paths.SnapshotFile));
    }

    private sealed record SnapshotCapture(
        string CharacterName,
        int ObjectIndex,
        int? WorldId,
        string? WorldName,
        string SnapshotPath,
        bool UseLiveData,
        bool UseMareFileCache,
        SnapshotLiveState? LiveState,
        SnapshotData? MareData,
        PenumbraPathState PenumbraPaths);

}
