using ECommons.ExcelServices;
using Luna;
using Snappy.Common;

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

    public async Task<string?> UpdateSnapshotAsync(ICharacter character, bool isLocalPlayer)
    {
        var capture = await Svc.Framework.RunOnFrameworkThread(() =>
            CaptureSnapshotState(character, isLocalPlayer)).ConfigureAwait(false);
        if (capture == null)
            return null;

        return await Task.Run(() => UpdateSnapshotFromCaptureAsync(capture)).ConfigureAwait(false);
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
            liveState = new SnapshotLiveState(charaName, objectIndex, _ipcManager.GetGlamourerState(character),
                _ipcManager.GetCustomizePlusScale(character), _ipcManager.GetMetaManipulations(objectIndex));
        }
        else
        {
            mareData = _mareSnapshotDataBuilder.BuildFromMare(character);
        }

        return new SnapshotCapture(charaName, objectIndex, resolvedWorldId, resolvedWorldName, snapshotPath,
            useLiveData, !_configuration.UseLiveSnapshotData && !isLocalPlayer, liveState, mareData);
    }

    private async Task<string?> UpdateSnapshotFromCaptureAsync(SnapshotCapture capture)
    {
        var now = DateTime.UtcNow;

        var snapshotData = BuildSnapshotData(capture);

        if (snapshotData == null) return null;

        if (!capture.UseLiveData)
        {
            BackfillSnapshotDataFromPenumbra(capture.ObjectIndex, snapshotData);
        }

        snapshotData = NormalizeSnapshotData(snapshotData);

        var paths = SnapshotPaths.From(capture.SnapshotPath);
        Directory.CreateDirectory(paths.RootPath);
        Directory.CreateDirectory(paths.FilesDirectory);

        var isNewSnapshot = !File.Exists(paths.SnapshotFile);

        var snapshotInfo = await JsonUtil.DeserializeAsync<SnapshotInfo>(paths.SnapshotFile) ??
                           new SnapshotInfo { SourceActor = capture.CharacterName };
        snapshotInfo.FileMaps ??= new List<FileMapEntry>();
        snapshotInfo.FileSwaps ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Backfill missing per-map manipulation data for older snapshots.
        BackfillManipulationStrings(snapshotInfo);
        UpdateSourceWorld(snapshotInfo, capture.WorldId, capture.WorldName);

        var glamourerHistory = await JsonUtil.DeserializeAsync<GlamourerHistory>(paths.GlamourerHistoryFile) ??
                               new GlamourerHistory();
        var customizeHistory = await JsonUtil.DeserializeAsync<CustomizeHistory>(paths.CustomizeHistoryFile) ??
                               new CustomizeHistory();

        var resolvedCurrentMap =
            EnsureBaseFileMap(snapshotInfo, glamourerHistory, customizeHistory, now);
        var resolvedCurrentFileSwaps =
            FileMapUtil.ResolveFileSwaps(snapshotInfo, snapshotInfo.CurrentFileMapId);

        var includeRemovals = !capture.UseLiveData || !_configuration.UsePenumbraIpcResourcePaths;

        foreach (var (gamePath, hash) in snapshotData.FileReplacements)
        {
            var existingFilePath = paths.FindAnyExistingHashedFilePath(hash);
            var hashedFilePath = existingFilePath ?? paths.GetPreferredHashedFilePath(hash, gamePath);
            if (!File.Exists(hashedFilePath))
            {
                string? sourceFile = null;
                if (capture.UseMareFileCache)
                    sourceFile = _ipcManager.GetMareFileCachePath(hash);
                if (string.IsNullOrEmpty(sourceFile))
                    snapshotData.ResolvedPaths.TryGetValue(hash, out sourceFile);

                if (!string.IsNullOrEmpty(sourceFile) && File.Exists(sourceFile))
                    await Task.Run(() => File.Copy(sourceFile, hashedFilePath, true));
                else
                    throw new FileNotFoundException(
                        $"Could not find source file for '{gamePath}' (hash: {hash}).");
            }
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

        SaveSnapshotToDisk(capture.SnapshotPath, snapshotInfo, glamourerHistory, customizeHistory);

        if (isNewSnapshot)
            Notify.Success($"New snapshot for '{capture.CharacterName}' created successfully.");
        else
            Notify.Success($"Snapshot for '{capture.CharacterName}' updated successfully.");

        return capture.SnapshotPath;
    }

    private SnapshotData? BuildSnapshotData(SnapshotCapture capture)
    {
        if (capture.UseLiveData)
        {
            if (capture.LiveState == null)
                return null;

            // Actor state is captured on the framework thread; file enumeration and hashing remain on the worker.
            return _configuration.UsePenumbraIpcResourcePaths
                ? _liveSnapshotDataBuilder.Build(capture.LiveState)
                : _collectionSnapshotDataBuilder.Build(capture.LiveState);
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

    private void BackfillSnapshotDataFromPenumbra(int objectIndex, SnapshotData snapshotData)
    {
        if (snapshotData.FileReplacements.Count == 0)
            return;

        var resolvedByGamePath = _ipcManager.PenumbraGetCollectionResolvedFiles(objectIndex);
        if (resolvedByGamePath.Count == 0)
        {
            var resourcePaths = _ipcManager.PenumbraGetGameObjectResourcePaths(objectIndex);
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

    public void SaveSnapshotToDisk(string snapshotPath, SnapshotInfo info, GlamourerHistory glamourerHistory,
        CustomizeHistory customizeHistory)
    {
        var paths = SnapshotPaths.From(snapshotPath);
        var snapshotSaved = JsonUtil.Serialize(info, paths.SnapshotFile);
        var glamourerSaved = JsonUtil.Serialize(glamourerHistory, paths.GlamourerHistoryFile);
        var customizeSaved = JsonUtil.Serialize(customizeHistory, paths.CustomizeHistoryFile);
        if (!snapshotSaved || !glamourerSaved || !customizeSaved)
            throw new IOException($"Failed to save all snapshot state files in '{snapshotPath}'.");
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
        SnapshotData? MareData);

}
