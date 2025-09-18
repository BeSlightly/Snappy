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
            using var lz4Stream = new LZ4Stream(fileStream, LZ4StreamMode.Decompress);
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

            var snapshotInfo = CreateSnapshotInfo(charaFile, snapshotDirName, gamePathToHashMap);
            var glamourerHistory = CreateGlamourerHistory(charaFile);
            var customizeHistory = CreateCustomizeHistory(charaFile);

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
            throw;
        }
    }

    private string CreateSnapshotDirectory(string? description)
    {
        var snapshotDirName = string.IsNullOrEmpty(description)
            ? $"MCDF_Import_{DateTime.Now:yyyyMMddHHmmss}"
            : description;

        var snapshotPath = Path.Combine(_configuration.WorkingDirectory, snapshotDirName);

        if (Directory.Exists(snapshotPath))
        {
            PluginLog.Debug("Snapshot from MCDF already existed, deleting");
            Directory.Delete(snapshotPath, true);
        }

        Directory.CreateDirectory(snapshotPath);
        return snapshotPath;
    }

    private static SnapshotInfo CreateSnapshotInfo(McdfHeader charaFile, string snapshotDirName,
        Dictionary<string, string> gamePathToHashMap)
    {
        return new SnapshotInfo
        {
            SourceActor = Path.GetFileName(snapshotDirName),
            SourceWorldId = null,
            LastUpdate = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            ManipulationString = charaFile.CharaFileData.ManipulationData,
            FileReplacements = gamePathToHashMap
        };
    }

    private static GlamourerHistory CreateGlamourerHistory(McdfHeader charaFile)
    {
        var history = new GlamourerHistory();
        history.Entries.Add(GlamourerHistoryEntry.Create(charaFile.CharaFileData.GlamourerData, "Imported from MCDF"));
        return history;
    }

    private static CustomizeHistory CreateCustomizeHistory(McdfHeader charaFile)
    {
        var history = new CustomizeHistory();
        var cplusData = charaFile.CharaFileData.CustomizePlusData;
        if (!string.IsNullOrEmpty(cplusData))
        {
            var cplusJson = Encoding.UTF8.GetString(Convert.FromBase64String(cplusData));
            var customizeEntry = CustomizeHistoryEntry.CreateFromBase64(cplusData, cplusJson, "Imported from MCDF");
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
            var hashedFilePath = Path.Combine(filesDir, hash + Constants.DataFileExtension);

            if (!File.Exists(hashedFilePath)) File.WriteAllBytes(hashedFilePath, buffer);

            foreach (var path in fileData.GamePaths) gamePathToHash[path] = hash;
        }

        return gamePathToHash;
    }
}