using System.Collections.Generic;
using System.IO;
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
        IReadOnlyDictionary<string, string>? resolvedFileMap = null,
        bool useReadableArchivePaths = false)
    {
        if (!Directory.Exists(sourceFilesDirectory)) return;

        var fileMap = resolvedFileMap ?? snapshotInfo.FileReplacements;
        if (useReadableArchivePaths)
        {
            AddSnapshotFilesWithReadablePaths(archive, sourceFilesDirectory, fileMap, filesDictionary);
            return;
        }

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

    private static void AddSnapshotFilesWithReadablePaths(
        ZipArchive archive,
        string sourceFilesDirectory,
        IReadOnlyDictionary<string, string> fileMap,
        Dictionary<string, string> filesDictionary)
    {
        var archivePathToHash = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (gamePath, hash) in fileMap)
        {
            if (string.IsNullOrWhiteSpace(gamePath) || string.IsNullOrWhiteSpace(hash))
                continue;

            var sourceFilePath = SnapshotBlobUtil.ResolveBlobPath(sourceFilesDirectory, hash, gamePath);
            if (!File.Exists(sourceFilePath))
                continue;

            var archiveFilePath = BuildReadableArchiveFilePath(gamePath);
            if (string.IsNullOrWhiteSpace(archiveFilePath))
                continue;

            if (archivePathToHash.TryGetValue(archiveFilePath, out var existingHash))
            {
                if (string.Equals(existingHash, hash, StringComparison.OrdinalIgnoreCase))
                {
                    filesDictionary[gamePath] = archiveFilePath.Replace('/', '\\');
                    continue;
                }

                archiveFilePath = BuildCollisionSafeArchivePath(archiveFilePath, hash, archivePathToHash);
            }

            archive.CreateEntryFromFile(sourceFilePath, archiveFilePath);
            archivePathToHash[archiveFilePath] = hash;
            filesDictionary[gamePath] = archiveFilePath.Replace('/', '\\');
        }
    }

    private static string BuildReadableArchiveFilePath(string gamePath)
    {
        var normalizedPath = gamePath.Replace('\\', '/').Trim();
        while (normalizedPath.StartsWith("/", StringComparison.Ordinal))
            normalizedPath = normalizedPath[1..];

        if (string.IsNullOrWhiteSpace(normalizedPath))
            return string.Empty;

        var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return string.Empty;

        var sanitizedSegments = new string[segments.Length];
        for (var i = 0; i < segments.Length; i++)
        {
            var fallback = i == segments.Length - 1 ? "file" : "dir";
            var sanitized = PathSanitizer.SanitizeFileSystemName(segments[i], fallback);
            sanitizedSegments[i] = sanitized is "." or ".." ? fallback : sanitized;
        }

        return "files/" + string.Join("/", sanitizedSegments);
    }

    private static string BuildCollisionSafeArchivePath(
        string archiveFilePath,
        string hash,
        IReadOnlyDictionary<string, string> existingEntries)
    {
        var directory = Path.GetDirectoryName(archiveFilePath)?.Replace('\\', '/') ?? "files";
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(archiveFilePath);
        var extension = Path.GetExtension(archiveFilePath);
        var shortHash = hash.Length > 8 ? hash[..8].ToLowerInvariant() : hash.ToLowerInvariant();

        var candidate = $"{directory}/{nameWithoutExtension}__{shortHash}{extension}";
        var counter = 2;
        while (existingEntries.ContainsKey(candidate))
        {
            candidate = $"{directory}/{nameWithoutExtension}__{shortHash}_{counter}{extension}";
            counter++;
        }

        return candidate;
    }
}
