using System.IO.Compression;
using Newtonsoft.Json.Linq;
using Snappy.Common;
using Snappy.Services.SnapshotManager;

namespace Snappy.Features.Pcp;

internal sealed class PcpImportService
{
    private readonly Configuration _configuration;
    private readonly ISnapshotFileService _snapshotFileService;
    private readonly Action _snapshotsUpdatedCallback;

    public PcpImportService(Configuration configuration, ISnapshotFileService snapshotFileService,
        Action snapshotsUpdatedCallback)
    {
        _configuration = configuration;
        _snapshotFileService = snapshotFileService;
        _snapshotsUpdatedCallback = snapshotsUpdatedCallback;
    }

    public void ImportPcp(string filePath)
    {
        if (!SnapshotImportUtil.TryAcquireImportLock(out var importLease))
        {
            Notify.Info("Another snapshot import is already in progress.");
            return;
        }

        string? snapshotPath = null;
        try
        {
            if (!File.Exists(filePath))
            {
                Notify.Error($"PCP file not found: {filePath}");
                return;
            }

            using var archive = ZipFile.OpenRead(filePath);

            // Read metadata
            var metadata = ArchiveUtil.ReadJsonEntry<PcpMetadata>(archive, "meta.json", Notify.Error,
                "Invalid PCP file: missing meta.json", "Failed to parse meta.json from PCP file.");
            if (metadata == null) return;

            // Read character data
            var characterData = ArchiveUtil.ReadJsonEntry<PcpCharacterData>(archive, "character.json", Notify.Error,
                "Invalid PCP file: missing character.json", "Failed to parse character.json from PCP file.");
            if (characterData == null) return;

            // Read mod data
            var modData = ArchiveUtil.ReadJsonEntry<PcpModData>(archive, "default_mod.json", Notify.Error,
                "Invalid PCP file: missing default_mod.json", "Failed to parse default_mod.json from PCP file.");
            if (modData == null) return;

            // Create snapshot
            snapshotPath = CreateSnapshotDirectory(metadata.Name);
            var paths = SnapshotPaths.From(snapshotPath);
            Directory.CreateDirectory(paths.FilesDirectory);

            // Extract files
            var gamePathToHashMap = ExtractFiles(archive, paths.FilesDirectory, modData);

            // Create snapshot info
            var snapshotInfo = CreateSnapshotInfo(characterData, gamePathToHashMap, modData);

            // Create Customize+ history
            var customizeHistory = new CustomizeHistory();
            if (characterData.CustomizePlus != null)
                customizeHistory = CreateCustomizeHistory(characterData, snapshotInfo.CurrentFileMapId);
            var customizeData = customizeHistory.Entries.LastOrDefault()?.CustomizeData ?? string.Empty;

            // Create Glamourer history
            var glamourerHistory = new GlamourerHistory();
            if (characterData.Glamourer != null)
                glamourerHistory = CreateGlamourerHistory(characterData, snapshotInfo.CurrentFileMapId, customizeData);

            // Save all data to disk
            _snapshotFileService.SaveSnapshotToDisk(paths.RootPath, snapshotInfo, glamourerHistory, customizeHistory);

            snapshotPath = null;
            _snapshotsUpdatedCallback();
            Notify.Success($"Successfully imported PCP: {metadata.Name}");
        }
        catch (Exception ex)
        {
            if (snapshotPath != null)
                RemoveIncompleteSnapshot(snapshotPath);

            Notify.Error($"Failed during PCP import for file: {Path.GetFileName(filePath)}\n{ex.Message}");
            PluginLog.Error($"Failed during PCP import for file: {Path.GetFileName(filePath)}: {ex}");
        }
        finally
        {
            importLease!.Dispose();
        }
    }

    private string CreateSnapshotDirectory(string description)
    {
        var snapshotDirName = SnapshotImportUtil.SanitizeDirectoryName(description, "PCP_Import");
        return SnapshotImportUtil.CreateUniqueSnapshotDirectory(_configuration.WorkingDirectory, snapshotDirName);
    }

    private static SnapshotInfo CreateSnapshotInfo(PcpCharacterData characterData,
        Dictionary<string, string> gamePathToHashMap, PcpModData modData)
    {
        // Convert PCP manipulations to Penumbra Base64 format
        var manipulationString = string.Empty;
        var manipulations = modData.Manipulations ?? [];
        if (manipulations.Count > 0)
            try
            {
                manipulationString = ConvertPcpManipulationsToPenumbraFormat(manipulations);
            }
            catch (Exception ex)
            {
                PluginLog.Warning($"Failed to process manipulations from PCP: {ex.Message}");
            }

        var actor = characterData.Actor ?? new PcpActor();
        var sourceActor = string.IsNullOrWhiteSpace(actor.PlayerName) ? "PCP Import" : actor.PlayerName;
        var sourceWorld = actor.HomeWorld is > 0 and < PcpActor.AnyWorld ? actor.HomeWorld : (int?)null;
        return SnapshotImportUtil.BuildSnapshotInfo(sourceActor, sourceWorld, manipulationString, gamePathToHashMap,
            NormalizeFileSwaps(modData.FileSwaps));
    }

    private static string ConvertPcpManipulationsToPenumbraFormat(List<JObject> pcpManipulations)
    {
        // Convert PCP manipulations to Penumbra's expected format
        // Penumbra expects: Base64(GZip(VersionByte + Data))

        // For now, we'll use version 0 format (JSON)
        var manipulationsJson = JsonConvert.SerializeObject(pcpManipulations);
        var jsonBytes = Encoding.UTF8.GetBytes(manipulationsJson);

        using var resultStream = new MemoryStream();
        using (var gzipStream = new GZipStream(resultStream, CompressionMode.Compress))
        {
            // Write version byte (0 for JSON format)
            gzipStream.WriteByte(0);
            // Write the JSON data
            gzipStream.Write(jsonBytes, 0, jsonBytes.Length);
        }

        return Convert.ToBase64String(resultStream.ToArray());
    }

    private static GlamourerHistory CreateGlamourerHistory(PcpCharacterData characterData, string? fileMapId,
        string? customizeData)
    {
        var history = new GlamourerHistory();
        if (characterData.Glamourer != null)
            try
            {
                // Parse the Glamourer object from PCP
                var glamourerObj = JObject.FromObject(characterData.Glamourer);

                // Check if this is the new Glamourer PCP format with Version and Design
                if (glamourerObj["Version"]?.ToObject<int>() == 1 && glamourerObj["Design"] is JObject designObj)
                {
                    // This is the new Glamourer PCP format - convert the Design to Base64
                    if (GlamourerDesignUtil.TryEncodeDesignJson(designObj, out var designBase64))
                        history.Entries.Add(GlamourerHistoryEntry.Create(designBase64, "Imported from PCP", fileMapId,
                            customizeData));
                    else
                        PluginLog.Warning("Failed to encode Glamourer Design data from PCP.");
                }
                else
                {
                    // This might be an older format or different structure, attempt to import as legacy data.
                    PluginLog.Debug("PCP Glamourer data is not in V1 format, attempting to import as legacy data.");
                    if (GlamourerDesignUtil.TryEncodeDesignJson(glamourerObj, out var designBase64))
                        history.Entries.Add(GlamourerHistoryEntry.Create(designBase64,
                            "Imported from PCP (Legacy Format)", fileMapId, customizeData));
                    else
                        PluginLog.Warning("Failed to encode Glamourer legacy data from PCP.");
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning($"Failed to process Glamourer data from PCP: {ex.Message}");
            }

        return history;
    }

    private static CustomizeHistory CreateCustomizeHistory(PcpCharacterData characterData, string? fileMapId)
    {
        var history = new CustomizeHistory();
        if (characterData.CustomizePlus != null)
            try
            {
                var customizePlusObj = JObject.FromObject(characterData.CustomizePlus);
                var source = customizePlusObj["Template"] ?? customizePlusObj;
                if (!CustomizePlusUtil.TryNormalizeIpcProfileJson(source.ToString(Formatting.None), out var profileJson))
                {
                    PluginLog.Warning("Failed to convert Customize+ PCP data to an IPC profile.");
                    return history;
                }

                var customizeBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(profileJson));
                history.Entries.Add(CustomizeHistoryEntry.CreateFromBase64(customizeBase64, profileJson,
                    customizePlusObj["Template"] == null ? "Imported from PCP (Legacy Format)" : "Imported from PCP",
                    fileMapId));
            }
            catch (Exception ex)
            {
                PluginLog.Warning($"Failed to process Customize+ data from PCP: {ex.Message}");
            }

        return history;
    }

    private static Dictionary<string, string> ExtractFiles(ZipArchive archive, string filesDir, PcpModData modData)
    {
        var gamePathToHashMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var archivePathToHash = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (rawGamePath, rawArchivePath) in modData.Files ?? [])
        {
            var gamePath = GamePathUtil.Normalize(rawGamePath);
            if (string.IsNullOrWhiteSpace(gamePath))
                throw new InvalidDataException($"PCP contains an invalid game path: '{rawGamePath}'.");

            var archivePath = ArchiveUtil.NormalizeArchivePath(rawArchivePath);
            if (string.IsNullOrWhiteSpace(archivePath))
                throw new InvalidDataException($"PCP file path is empty for '{gamePath}'.");

            if (!archivePathToHash.TryGetValue(archivePath, out var hash))
            {
                var entry = ArchiveUtil.FindEntry(archive, archivePath);
                if (entry == null)
                    throw new InvalidDataException(
                        $"PCP archive entry '{rawArchivePath}' for '{gamePath}' is missing.");

                hash = ExtractEntryToBlob(entry, filesDir, gamePath);
                archivePathToHash[archivePath] = hash;
            }

            gamePathToHashMap[gamePath] = hash;
        }

        return gamePathToHashMap;
    }

    private static string ExtractEntryToBlob(ZipArchiveEntry entry, string filesDir, string gamePath)
    {
        var temporaryPath = Path.Combine(filesDir, $".{Guid.NewGuid():N}.tmp");
        try
        {
            using (var entryStream = entry.Open())
            using (var outputStream = File.Create(temporaryPath))
                entryStream.CopyTo(outputStream);

            var hash = PluginUtil.GetFileHash(temporaryPath);
            var existingPath = SnapshotBlobUtil.FindAnyExistingBlobPath(filesDir, hash);
            var outputPath = existingPath ?? SnapshotBlobUtil.GetPreferredBlobPath(filesDir, hash, gamePath);
            if (!File.Exists(outputPath))
                File.Move(temporaryPath, outputPath);

            return hash;
        }
        finally
        {
            AtomicFileUtil.TryDelete(temporaryPath);
        }
    }

    private static Dictionary<string, string> NormalizeFileSwaps(Dictionary<string, string>? fileSwaps)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (fileSwaps == null)
            return result;

        foreach (var (rawGamePath, swapPath) in fileSwaps)
        {
            var gamePath = GamePathUtil.Normalize(rawGamePath);
            var normalizedSwapPath = GamePathUtil.Normalize(swapPath);
            if (!string.IsNullOrWhiteSpace(gamePath) && !string.IsNullOrWhiteSpace(normalizedSwapPath))
                result[gamePath] = normalizedSwapPath;
        }

        return result;
    }

    private static void RemoveIncompleteSnapshot(string snapshotPath)
    {
        try
        {
            if (Directory.Exists(snapshotPath))
                Directory.Delete(snapshotPath, true);
        }
        catch (Exception cleanupException)
        {
            PluginLog.Warning($"Failed to remove incomplete PCP import '{snapshotPath}': {cleanupException.Message}");
        }
    }
}
