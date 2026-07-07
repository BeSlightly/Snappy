using LZ4;
using Snappy.Common;
using Snappy.Services.SnapshotManager;

namespace Snappy.Features.Mcdf;

public class McdfManager : IMcdfManager
{
    private readonly Configuration _configuration;
    private readonly ISnapshotFileService _snapshotFileService;
    private readonly Action _snapshotsUpdatedCallback;

    public McdfManager(Configuration configuration, ISnapshotFileService snapshotFileService,
        Action snapshotsUpdatedCallback)
    {
        _configuration = configuration;
        _snapshotFileService = snapshotFileService;
        _snapshotsUpdatedCallback = snapshotsUpdatedCallback;
    }

    public void ImportMcdf(string filePath)
    {
        try
        {
            using var fileStream = File.OpenRead(filePath);
            using var lz4Stream = new LZ4Stream(fileStream, LZ4StreamMode.Decompress,
                LZ4StreamFlags.HighCompression);
            using var memoryStream = new MemoryStream();
            lz4Stream.CopyTo(memoryStream);
            memoryStream.Position = 0;

            using var reader = new BinaryReader(memoryStream);

            var charaFile = McdfHeader.FromBinaryReader(filePath, reader);
            PluginLog.Debug("Read Mare Chara File. Version: " + charaFile.Version);

            var snapshotDirName = CreateSnapshotDirectory(charaFile.CharaFileData.Description);
            var paths = SnapshotPaths.From(snapshotDirName);
            Directory.CreateDirectory(paths.FilesDirectory);

            var gamePathToHashMap = ExtractAndHashMapFiles(charaFile, reader, paths.FilesDirectory);
            var fileSwaps = ExtractFileSwaps(charaFile);
            foreach (var gamePath in fileSwaps.Keys)
                gamePathToHashMap.Remove(gamePath); // Brio applies swaps after files, so swaps win on collisions.

            var snapshotInfo = CreateSnapshotInfo(charaFile, snapshotDirName, gamePathToHashMap, fileSwaps);
            var customizeHistory = CreateCustomizeHistory(charaFile, snapshotInfo.CurrentFileMapId);
            var customizeData = customizeHistory.Entries.LastOrDefault()?.CustomizeData ?? string.Empty;
            var glamourerHistory = CreateGlamourerHistory(charaFile, snapshotInfo.CurrentFileMapId, customizeData);

            _snapshotFileService
                .SaveSnapshotToDisk(paths.RootPath, snapshotInfo, glamourerHistory, customizeHistory);

            Notify.Success(
                $"Successfully imported '{Path.GetFileName(filePath)}' as new snapshot '{Path.GetFileName(snapshotDirName)}'.");
            _snapshotsUpdatedCallback();
        }
        catch (Exception ex)
        {
            Notify.Error($"Failed during MCDF extraction for file: {Path.GetFileName(filePath)}\n{ex.Message}");
            PluginLog.Error($"Failed during MCDF extraction for file: {Path.GetFileName(filePath)}: {ex}");
        }
    }

    public async Task ExportMcdf(string snapshotPath, string outputPath, GlamourerHistoryEntry? selectedGlamourer,
        CustomizeHistoryEntry? selectedCustomize)
    {
        try
        {
            var paths = SnapshotPaths.From(snapshotPath);
            var snapshotInfo = await JsonUtil.DeserializeAsync<SnapshotInfo>(paths.SnapshotFile);
            if (snapshotInfo == null)
            {
                Notify.Error("Failed to load snapshot info for MCDF export.");
                return;
            }

            var glamourerHistory = await JsonUtil.DeserializeAsync<GlamourerHistory>(paths.GlamourerHistoryFile) ??
                                   new GlamourerHistory();
            var customizeHistory = await JsonUtil.DeserializeAsync<CustomizeHistory>(paths.CustomizeHistoryFile) ??
                                   new CustomizeHistory();

            var glamourerEntry = selectedGlamourer ??
                                  (glamourerHistory.Entries.Count > 0 ? glamourerHistory.Entries.Last() : null);
            var customizeEntry = selectedCustomize ??
                                 (customizeHistory.Entries.Count > 0 ? customizeHistory.Entries.Last() : null);
            var fileMapId = glamourerEntry?.FileMapId ?? customizeEntry?.FileMapId ?? snapshotInfo.CurrentFileMapId;
            var resolvedFileMap = FileMapUtil.ResolveFileMapWithEmptyFallback(snapshotInfo, fileMapId);
            var resolvedFileSwaps = FileMapUtil.ResolveFileSwaps(snapshotInfo, fileMapId);
            foreach (var gamePath in resolvedFileSwaps.Keys)
                resolvedFileMap.Remove(gamePath);
            var resolvedManipulations = FileMapUtil.ResolveManipulation(snapshotInfo, fileMapId);

            var output = Path.ChangeExtension(outputPath, ".mcdf");
            using var fileStream = new FileStream(output, FileMode.Create, FileAccess.Write, FileShare.None);
            using var lz4Stream = new LZ4Stream(fileStream, LZ4StreamMode.Compress,
                LZ4StreamFlags.HighCompression);
            using var writer = new BinaryWriter(lz4Stream, Encoding.UTF8, false);

            var files = BuildMcdfFiles(paths.FilesDirectory, resolvedFileMap, out var fileBytes);
            var header = new McdfHeader(McdfHeader.CurrentVersion, new McdfData
            {
                Description = Path.GetFileName(snapshotPath),
                GlamourerData = glamourerEntry?.GlamourerString ?? string.Empty,
                CustomizePlusData = ResolveCustomizePlusData(customizeEntry, glamourerEntry),
                ManipulationData = resolvedManipulations,
                Files = files,
                FileSwaps = BuildMcdfFileSwaps(resolvedFileSwaps)
            });

            header.WriteToStream(writer);
            foreach (var data in fileBytes)
                writer.Write(data);

            Notify.Success($"Successfully exported MCDF: {output}");
        }
        catch (Exception ex)
        {
            Notify.Error($"Failed during MCDF export: {ex.Message}");
            PluginLog.Error($"Failed during MCDF export for '{snapshotPath}': {ex}");
        }
    }

    private string CreateSnapshotDirectory(string? description)
    {
        var snapshotDirName = SnapshotImportUtil.SanitizeDirectoryName(description, "MCDF_Import");

        return SnapshotImportUtil.CreateUniqueSnapshotDirectory(
            _configuration.WorkingDirectory,
            snapshotDirName,
            name => PluginLog.Debug($"Snapshot directory already exists, trying {name}"));
    }

    private static SnapshotInfo CreateSnapshotInfo(McdfHeader charaFile, string snapshotDirName,
        Dictionary<string, string> gamePathToHashMap, Dictionary<string, string> fileSwaps)
    {
        return SnapshotImportUtil.BuildSnapshotInfo(
            Path.GetFileName(snapshotDirName),
            null,
            charaFile.CharaFileData.ManipulationData,
            gamePathToHashMap,
            fileSwaps);
    }

    private static GlamourerHistory CreateGlamourerHistory(McdfHeader charaFile, string? fileMapId,
        string? customizeData)
    {
        var history = new GlamourerHistory();
        history.Entries.Add(
            GlamourerHistoryEntry.Create(charaFile.CharaFileData.GlamourerData, "Imported from MCDF", fileMapId,
                customizeData));
        return history;
    }

    private static CustomizeHistory CreateCustomizeHistory(McdfHeader charaFile, string? fileMapId)
    {
        var history = new CustomizeHistory();
        var cplusData = charaFile.CharaFileData.CustomizePlusData;
        if (!string.IsNullOrEmpty(cplusData))
        {
            var cplusJson = Encoding.UTF8.GetString(Convert.FromBase64String(cplusData));
            var customizeEntry =
                CustomizeHistoryEntry.CreateFromBase64(cplusData, cplusJson, "Imported from MCDF", fileMapId);
            history.Entries.Add(customizeEntry);
        }

        return history;
    }

    private static Dictionary<string, string> ExtractAndHashMapFiles(McdfHeader charaFileHeader, BinaryReader reader,
        string filesDir)
    {
        var gamePathToHash = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var fileData in charaFileHeader.CharaFileData.Files)
        {
            var length = fileData.Length;
            if (length == 0) continue;

            var buffer = reader.ReadBytes((int)length);
            if (buffer.Length != length)
            {
                PluginLog.Error($"MCDF Read Error: Expected {length} bytes, got {buffer.Length}. File may be corrupt.");
                continue;
            }

            var hash = PluginUtil.GetFileHash(buffer);
            var representativeGamePath = fileData.GamePaths.FirstOrDefault() ?? string.Empty;
            var existingPath = SnapshotBlobUtil.FindAnyExistingBlobPath(filesDir, hash);
            var hashedFilePath = existingPath ?? SnapshotBlobUtil.GetPreferredBlobPath(filesDir, hash, representativeGamePath);

            if (!File.Exists(hashedFilePath)) File.WriteAllBytes(hashedFilePath, buffer);

            foreach (var path in fileData.GamePaths) gamePathToHash[path] = hash;
        }

        return gamePathToHash;
    }

    private static Dictionary<string, string> ExtractFileSwaps(McdfHeader charaFileHeader)
    {
        var fileSwaps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var swap in charaFileHeader.CharaFileData.FileSwaps ?? [])
        {
            if (string.IsNullOrWhiteSpace(swap.FileSwapPath))
                continue;

            foreach (var gamePath in swap.GamePaths)
                if (!string.IsNullOrWhiteSpace(gamePath))
                    fileSwaps[gamePath] = swap.FileSwapPath;
        }

        return fileSwaps;
    }

    private static List<McdfData.FileData> BuildMcdfFiles(string filesDirectory,
        IReadOnlyDictionary<string, string> resolvedFileMap, out List<byte[]> fileBytes)
    {
        var files = new List<McdfData.FileData>();
        fileBytes = [];

        foreach (var group in resolvedFileMap
                     .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value))
                     .GroupBy(kvp => kvp.Value, StringComparer.OrdinalIgnoreCase))
        {
            var gamePaths = group.Select(kvp => kvp.Key).Where(path => !string.IsNullOrWhiteSpace(path)).ToList();
            if (gamePaths.Count == 0)
                continue;

            var hash = group.Key;
            var blobPath = SnapshotBlobUtil.ResolveBlobPath(filesDirectory, hash, gamePaths[0]);
            if (!File.Exists(blobPath))
            {
                PluginLog.Warning($"Skipping missing MCDF export blob '{hash}' for '{gamePaths[0]}'.");
                continue;
            }

            var data = File.ReadAllBytes(blobPath);
            files.Add(new McdfData.FileData(gamePaths, data.Length, hash));
            fileBytes.Add(data);
        }

        return files;
    }

    private static List<McdfData.FileSwap> BuildMcdfFileSwaps(
        IReadOnlyDictionary<string, string> resolvedFileSwaps)
    {
        return resolvedFileSwaps
            .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Key) && !string.IsNullOrWhiteSpace(kvp.Value))
            .GroupBy(kvp => kvp.Value, StringComparer.OrdinalIgnoreCase)
            .Select(group => new McdfData.FileSwap(group.Select(kvp => kvp.Key).ToArray(), group.Key))
            .ToList();
    }

    private static string ResolveCustomizePlusData(CustomizeHistoryEntry? customizeEntry,
        GlamourerHistoryEntry? glamourerEntry)
    {
        if (!string.IsNullOrWhiteSpace(customizeEntry?.CustomizeData))
            return customizeEntry.CustomizeData;

        if (!string.IsNullOrWhiteSpace(glamourerEntry?.CustomizeData))
            return glamourerEntry.CustomizeData;

        return string.Empty;
    }
}
