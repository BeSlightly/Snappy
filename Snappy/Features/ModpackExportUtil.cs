using System.IO.Compression;
using Snappy.Common;
using Snappy.Models;

namespace Snappy.Features;

public static class ModpackExportUtil
{
    public static void AddSnapshotFilesToArchive(
        ZipArchive archive,
        SnapshotInfo snapshotInfo,
        string sourceFilesDirectory,
        Dictionary<string, string> filesDictionary)
    {
        if (!Directory.Exists(sourceFilesDirectory)) return;

        foreach (var file in Directory.GetFiles(sourceFilesDirectory, "*", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileName(file);
            var hash = Path.GetFileNameWithoutExtension(fileName);

            // Find the corresponding game path from the snapshot info  
            var gamePath = snapshotInfo.FileReplacements
                .FirstOrDefault(kvp => kvp.Value == hash).Key;

            if (!string.IsNullOrEmpty(gamePath))
            {
                var archiveFilePath = $"files/{fileName}";
                archive.CreateEntryFromFile(file, archiveFilePath);
                filesDictionary[gamePath] = archiveFilePath.Replace('/', '\\'); // Penumbra expects backslashes  
            }
        }
    }
}