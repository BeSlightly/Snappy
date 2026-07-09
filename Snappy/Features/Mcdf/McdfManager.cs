using LZ4;
using System.Security.Cryptography;
using Snappy.Common;
using Snappy.Services.SnapshotManager;

namespace Snappy.Features.Mcdf;

public class McdfManager : IMcdfManager
{
    private const int CopyBufferSize = 81920;

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
        string? createdSnapshotPath = null;
        try
        {
            if (!File.Exists(filePath))
            {
                Notify.Error($"MCDF file not found: {filePath}");
                return;
            }

            using var fileStream = File.OpenRead(filePath);
            using var lz4Stream = new LZ4Stream(fileStream, LZ4StreamMode.Decompress,
                LZ4StreamFlags.HighCompression);
            using var reader = new BinaryReader(lz4Stream);

            var charaFile = McdfHeader.FromBinaryReader(reader);
            PluginLog.Debug("Read Mare Chara File. Version: " + charaFile.Version);

            createdSnapshotPath = CreateSnapshotDirectory(charaFile.CharaFileData.Description);
            var paths = SnapshotPaths.From(createdSnapshotPath);
            Directory.CreateDirectory(paths.FilesDirectory);

            var gamePathToHashMap = ExtractAndHashMapFiles(charaFile, reader, paths.FilesDirectory);
            var fileSwaps = ExtractFileSwaps(charaFile);
            foreach (var gamePath in fileSwaps.Keys)
                gamePathToHashMap.Remove(gamePath); // Brio applies swaps after files, so swaps win on collisions.

            var snapshotInfo = CreateSnapshotInfo(charaFile, createdSnapshotPath, gamePathToHashMap, fileSwaps);
            var customizeHistory = CreateCustomizeHistory(charaFile, snapshotInfo.CurrentFileMapId);
            var customizeData = customizeHistory.Entries.LastOrDefault()?.CustomizeData ?? string.Empty;
            var glamourerHistory = CreateGlamourerHistory(charaFile, snapshotInfo.CurrentFileMapId, customizeData);

            _snapshotFileService.SaveSnapshotToDisk(paths.RootPath, snapshotInfo, glamourerHistory, customizeHistory);

            var importedSnapshotName = Path.GetFileName(createdSnapshotPath);
            createdSnapshotPath = null;
            _snapshotsUpdatedCallback();
            Notify.Success(
                $"Successfully imported '{Path.GetFileName(filePath)}' as new snapshot '{importedSnapshotName}'.");
        }
        catch (Exception ex)
        {
            if (createdSnapshotPath != null)
                RemoveIncompleteSnapshot(createdSnapshotPath);

            Notify.Error($"Failed during MCDF extraction for file: {Path.GetFileName(filePath)}\n{ex.Message}");
            PluginLog.Error($"Failed during MCDF extraction for file: {Path.GetFileName(filePath)}: {ex}");
        }
    }

    public async Task ExportMcdf(string snapshotPath, string outputPath, GlamourerHistoryEntry? selectedGlamourer,
        CustomizeHistoryEntry? selectedCustomize)
    {
        string? temporaryOutput = null;
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

            var glamourerEntry = selectedGlamourer ?? glamourerHistory.Entries.LastOrDefault();
            var customizeEntry = selectedCustomize ?? customizeHistory.Entries.LastOrDefault();
            var fileMapId = glamourerEntry?.FileMapId ?? customizeEntry?.FileMapId ?? snapshotInfo.CurrentFileMapId;
            var resolvedFileMap = FileMapUtil.ResolveFileMapWithEmptyFallback(snapshotInfo, fileMapId);
            var resolvedFileSwaps = FileMapUtil.ResolveFileSwapsWithEmptyFallback(snapshotInfo, fileMapId);
            foreach (var gamePath in resolvedFileSwaps.Keys)
                resolvedFileMap.Remove(gamePath);
            var resolvedManipulations = FileMapUtil.ResolveManipulation(snapshotInfo, fileMapId);

            var files = BuildMcdfFiles(paths.FilesDirectory, resolvedFileMap);
            var header = new McdfHeader(McdfHeader.CurrentVersion, new McdfData
            {
                Description = Path.GetFileName(snapshotPath),
                GlamourerData = glamourerEntry?.GlamourerString ?? string.Empty,
                CustomizePlusData = ResolveCustomizePlusData(customizeEntry, glamourerEntry),
                ManipulationData = resolvedManipulations,
                Files = files.Select(file => file.Data).ToList(),
                FileSwaps = BuildMcdfFileSwaps(resolvedFileSwaps)
            });

            var mcdfOutputPath = Path.ChangeExtension(outputPath, ".mcdf");
            temporaryOutput = AtomicFileUtil.CreateTemporaryOutputPath(mcdfOutputPath);
            using (var fileStream = new FileStream(temporaryOutput, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var lz4Stream = new LZ4Stream(fileStream, LZ4StreamMode.Compress, LZ4StreamFlags.HighCompression))
            using (var writer = new BinaryWriter(lz4Stream, Encoding.UTF8, true))
            {
                header.WriteToStream(writer);
                writer.Flush();
                foreach (var file in files)
                    CopyFileExactly(file.Path, file.Data.Length, lz4Stream);
            }

            AtomicFileUtil.Complete(temporaryOutput, mcdfOutputPath);
            temporaryOutput = null;
            Notify.Success($"Successfully exported MCDF: {mcdfOutputPath}");
        }
        catch (Exception ex)
        {
            Notify.Error($"Failed during MCDF export: {ex.Message}");
            PluginLog.Error($"Failed during MCDF export for '{snapshotPath}': {ex}");
        }
        finally
        {
            if (temporaryOutput != null)
                AtomicFileUtil.TryDelete(temporaryOutput);
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
        => SnapshotImportUtil.BuildSnapshotInfo(
            Path.GetFileName(snapshotDirName),
            null,
            charaFile.CharaFileData.ManipulationData,
            gamePathToHashMap,
            fileSwaps);

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
        var customizeData = charaFile.CharaFileData.CustomizePlusData;
        if (string.IsNullOrWhiteSpace(customizeData))
            return history;

        var profileJson = DecodeCustomizeData(customizeData);
        var normalizedData = CustomizePlusUtil.TryNormalizeIpcProfileJson(profileJson, out var normalizedProfileJson)
            ? Convert.ToBase64String(Encoding.UTF8.GetBytes(normalizedProfileJson))
            : customizeData;
        history.Entries.Add(CustomizeHistoryEntry.CreateFromBase64(normalizedData,
            normalizedData == customizeData ? profileJson : normalizedProfileJson, "Imported from MCDF", fileMapId));
        return history;
    }

    private static Dictionary<string, string> ExtractAndHashMapFiles(McdfHeader charaFileHeader, BinaryReader reader,
        string filesDir)
    {
        var gamePathToHash = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var buffer = new byte[CopyBufferSize];
        foreach (var fileData in charaFileHeader.CharaFileData.Files ?? [])
        {
            if (fileData.Length < 0)
                throw new InvalidDataException($"MCDF file entry has invalid length {fileData.Length}.");
            if (fileData.Length == 0)
                continue;

            var gamePaths = (fileData.GamePaths ?? [])
                .Select(GamePathUtil.Normalize)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (gamePaths.Length == 0)
            {
                SkipBytes(reader, fileData.Length, buffer);
                continue;
            }

            var hash = ExtractFileToBlob(reader, fileData.Length, filesDir, gamePaths[0], buffer);
            foreach (var path in gamePaths)
                gamePathToHash[path] = hash;
        }

        return gamePathToHash;
    }

    private static string ExtractFileToBlob(BinaryReader reader, int length, string filesDir, string gamePath,
        byte[] buffer)
    {
        var temporaryPath = Path.Combine(filesDir, $".{Guid.NewGuid():N}.tmp");
        try
        {
            using (var output = File.Create(temporaryPath))
            using (var sha1 = SHA1.Create())
            {
                var remaining = length;
                while (remaining > 0)
                {
                    var read = reader.Read(buffer, 0, Math.Min(buffer.Length, remaining));
                    if (read <= 0)
                        throw new EndOfStreamException("MCDF ended before all file data could be read.");

                    output.Write(buffer, 0, read);
                    sha1.TransformBlock(buffer, 0, read, buffer, 0);
                    remaining -= read;
                }

                sha1.TransformFinalBlock([], 0, 0);
                var hash = Convert.ToHexString(sha1.Hash!);
                var existingPath = SnapshotBlobUtil.FindAnyExistingBlobPath(filesDir, hash);
                var blobPath = existingPath ?? SnapshotBlobUtil.GetPreferredBlobPath(filesDir, hash, gamePath);
                output.Flush();
                output.Close();
                if (!File.Exists(blobPath))
                    File.Move(temporaryPath, blobPath);

                return hash;
            }
        }
        finally
        {
            AtomicFileUtil.TryDelete(temporaryPath);
        }
    }

    private static void SkipBytes(BinaryReader reader, int length, byte[] buffer)
    {
        var remaining = length;
        while (remaining > 0)
        {
            var read = reader.Read(buffer, 0, Math.Min(buffer.Length, remaining));
            if (read <= 0)
                throw new EndOfStreamException("MCDF ended before all file data could be skipped.");
            remaining -= read;
        }
    }

    private static Dictionary<string, string> ExtractFileSwaps(McdfHeader charaFileHeader)
    {
        var fileSwaps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var swap in charaFileHeader.CharaFileData.FileSwaps ?? [])
        {
            if (string.IsNullOrWhiteSpace(swap.FileSwapPath))
                continue;

            var fileSwapPath = GamePathUtil.Normalize(swap.FileSwapPath);
            if (string.IsNullOrWhiteSpace(fileSwapPath))
                continue;

            foreach (var rawGamePath in swap.GamePaths ?? [])
            {
                var gamePath = GamePathUtil.Normalize(rawGamePath);
                if (!string.IsNullOrWhiteSpace(gamePath))
                    fileSwaps[gamePath] = fileSwapPath;
            }
        }

        return fileSwaps;
    }

    private static List<McdfExportFile> BuildMcdfFiles(string filesDirectory,
        IReadOnlyDictionary<string, string> resolvedFileMap)
    {
        var files = new List<McdfExportFile>();
        foreach (var group in resolvedFileMap
                     .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Key) && !string.IsNullOrWhiteSpace(kvp.Value))
                     .GroupBy(kvp => kvp.Value, StringComparer.OrdinalIgnoreCase))
        {
            var gamePaths = group.Select(kvp => kvp.Key).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var blobPath = gamePaths
                .Select(gamePath => SnapshotBlobUtil.ResolveBlobPath(filesDirectory, group.Key, gamePath))
                .FirstOrDefault(File.Exists);
            if (blobPath == null)
            {
                PluginLog.Warning($"Skipping missing MCDF export blob '{group.Key}' for '{gamePaths[0]}'.");
                continue;
            }

            var length = new FileInfo(blobPath).Length;
            if (length > int.MaxValue)
            {
                PluginLog.Warning($"Skipping MCDF export blob '{group.Key}' because it exceeds the MCDF size limit.");
                continue;
            }

            files.Add(new McdfExportFile(new McdfData.FileData(gamePaths, (int)length, group.Key), blobPath));
        }

        return files;
    }

    private static List<McdfData.FileSwap> BuildMcdfFileSwaps(
        IReadOnlyDictionary<string, string> resolvedFileSwaps)
        => resolvedFileSwaps
            .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Key) && !string.IsNullOrWhiteSpace(kvp.Value))
            .GroupBy(kvp => kvp.Value, StringComparer.OrdinalIgnoreCase)
            .Select(group => new McdfData.FileSwap(group.Select(kvp => kvp.Key).ToArray(), group.Key))
            .ToList();

    private static void CopyFileExactly(string sourcePath, int length, Stream destination)
    {
        using var source = File.OpenRead(sourcePath);
        var buffer = new byte[CopyBufferSize];
        var remaining = length;
        while (remaining > 0)
        {
            var read = source.Read(buffer, 0, Math.Min(buffer.Length, remaining));
            if (read <= 0)
                throw new EndOfStreamException($"MCDF export blob '{sourcePath}' changed while being exported.");
            destination.Write(buffer, 0, read);
            remaining -= read;
        }

        if (source.ReadByte() != -1)
            throw new IOException($"MCDF export blob '{sourcePath}' changed while being exported.");
    }

    private static string ResolveCustomizePlusData(CustomizeHistoryEntry? customizeEntry,
        GlamourerHistoryEntry? glamourerEntry)
    {
        if (!string.IsNullOrWhiteSpace(customizeEntry?.CustomizeData))
            return customizeEntry.CustomizeData;
        return !string.IsNullOrWhiteSpace(glamourerEntry?.CustomizeData) ? glamourerEntry.CustomizeData : string.Empty;
    }

    private static string DecodeCustomizeData(string customizeData)
    {
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(customizeData));
        }
        catch (FormatException)
        {
            return customizeData;
        }
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
            PluginLog.Warning($"Failed to remove incomplete MCDF import '{snapshotPath}': {cleanupException.Message}");
        }
    }

    private sealed record McdfExportFile(McdfData.FileData Data, string Path);
}
