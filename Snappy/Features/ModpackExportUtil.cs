using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using Snappy.Common;
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

        foreach (var file in Directory.GetFiles(sourceFilesDirectory, "*", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileName(file);
            var hash = Path.GetFileNameWithoutExtension(fileName);

            if (!gamePathsByHash.TryGetValue(hash, out var gamePaths) || gamePaths.Count == 0)
                continue;

            var archiveFilePath = $"files/{fileName}";
            archive.CreateEntryFromFile(file, archiveFilePath);

            foreach (var gamePath in gamePaths.Distinct(StringComparer.OrdinalIgnoreCase))
                filesDictionary[gamePath] = archiveFilePath.Replace('/', '\\'); // Penumbra expects backslashes
        }
    }
}
