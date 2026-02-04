using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using Snappy.Common;
using Snappy.Common.Utilities;
using Snappy.Models;

namespace Snappy.Features;

public static class ModpackExportUtil
{
    public static void AddSnapshotFilesToArchive(
        ZipArchive archive,
        SnapshotInfo snapshotInfo,
        string sourceFilesDirectory,
        Dictionary<string, string> filesDictionary,
        IReadOnlyDictionary<string, string>? resolvedFileMap = null)
    {
        if (!Directory.Exists(sourceFilesDirectory)) return;

        var fileMap = resolvedFileMap ?? snapshotInfo.FileReplacements;
        var gamePathsByHash = fileMap
            .GroupBy(kvp => kvp.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(kvp => kvp.Key).ToList(), StringComparer.OrdinalIgnoreCase);

        var exportedHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(sourceFilesDirectory, "*", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileName(file);
            var hash = Path.GetFileNameWithoutExtension(fileName);

            if (!gamePathsByHash.TryGetValue(hash, out var gamePaths) || gamePaths.Count == 0)
                continue;
            if (!exportedHashes.Add(hash))
                continue;

            var archiveFileName = BuildArchiveFileName(hash, fileName, gamePaths);
            var archiveFilePath = $"files/{archiveFileName}";
            var preferredPath = Path.Combine(sourceFilesDirectory, archiveFileName);
            var sourceFilePath = File.Exists(preferredPath) ? preferredPath : file;
            archive.CreateEntryFromFile(sourceFilePath, archiveFilePath);

            foreach (var gamePath in gamePaths.Distinct(StringComparer.OrdinalIgnoreCase))
                filesDictionary[gamePath] = archiveFilePath.Replace('/', '\\'); // Penumbra expects backslashes
        }
    }

    private static string BuildArchiveFileName(string hash, string existingFileName, IReadOnlyList<string> gamePaths)
    {
        var existingExtension = Path.GetExtension(existingFileName);
        if (!string.IsNullOrEmpty(existingExtension)
            && !existingExtension.Equals(Constants.DataFileExtension, StringComparison.OrdinalIgnoreCase))
            return hash + existingExtension.ToLowerInvariant();

        foreach (var gamePath in gamePaths)
        {
            var extension = SnapshotBlobUtil.GetPreferredExtensionFromGamePath(gamePath);
            if (!extension.Equals(Constants.DataFileExtension, StringComparison.OrdinalIgnoreCase))
                return hash + extension;
        }

        return hash + (string.IsNullOrEmpty(existingExtension)
            ? Constants.DataFileExtension
            : existingExtension.ToLowerInvariant());
    }
}
