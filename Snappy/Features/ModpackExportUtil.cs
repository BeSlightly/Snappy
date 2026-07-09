using System.IO.Compression;
using Snappy.Common;

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

        var archivePathToHash = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in fileMap
                     .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Key) && !string.IsNullOrWhiteSpace(kvp.Value))
                     .GroupBy(kvp => kvp.Value, StringComparer.OrdinalIgnoreCase))
        {
            var hash = group.Key;
            var gamePaths = group.Select(kvp => kvp.Key).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var sourceFilePath = gamePaths
                .Select(gamePath => SnapshotBlobUtil.ResolveBlobPath(sourceFilesDirectory, hash, gamePath))
                .FirstOrDefault(File.Exists);
            if (sourceFilePath == null)
            {
                PluginLog.Warning($"Skipping missing export blob '{hash}' for '{gamePaths[0]}'.");
                continue;
            }

            var archiveFileName = BuildArchiveFileName(hash, Path.GetFileName(sourceFilePath), gamePaths);
            var archiveFilePath = $"files/{archiveFileName}";
            if (archivePathToHash.TryGetValue(archiveFilePath, out var existingHash)
                && !string.Equals(existingHash, hash, StringComparison.OrdinalIgnoreCase))
                archiveFilePath = BuildCollisionSafeArchivePath(archiveFilePath, hash, archivePathToHash);

            archive.CreateEntryFromFile(sourceFilePath, archiveFilePath);
            archivePathToHash[archiveFilePath] = hash;

            foreach (var gamePath in gamePaths)
                filesDictionary[gamePath] = archiveFilePath.Replace('/', '\\'); // Penumbra expects backslashes
        }
    }

    private static string BuildArchiveFileName(string hash, string existingFileName, IReadOnlyList<string> gamePaths)
    {
        var safeHash = PathSanitizer.SanitizeFileSystemName(hash, "file");
        var existingExtension = Path.GetExtension(existingFileName);
        if (!string.IsNullOrEmpty(existingExtension)
            && !existingExtension.Equals(Constants.DataFileExtension, StringComparison.OrdinalIgnoreCase))
            return safeHash + existingExtension.ToLowerInvariant();

        foreach (var gamePath in gamePaths)
        {
            var extension = SnapshotBlobUtil.GetPreferredExtensionFromGamePath(gamePath);
            if (!extension.Equals(Constants.DataFileExtension, StringComparison.OrdinalIgnoreCase))
                return safeHash + extension;
        }

        return safeHash + (string.IsNullOrEmpty(existingExtension)
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
        shortHash = PathSanitizer.SanitizeFileSystemName(shortHash, "file");

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
