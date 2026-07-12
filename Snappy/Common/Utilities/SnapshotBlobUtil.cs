namespace Snappy.Common.Utilities;

public static class SnapshotBlobUtil
{
    public static bool TryNormalizeBlobId(string? value, out string blobId)
    {
        blobId = value?.Trim() ?? string.Empty;
        if (blobId.Length is not (40 or 64) || blobId.Any(character => !Uri.IsHexDigit(character)))
        {
            blobId = string.Empty;
            return false;
        }

        return true;
    }

    public static string GetPreferredExtensionFromGamePath(string gamePath)
    {
        var ext = Path.GetExtension(gamePath);
        return NormalizeExtension(ext);
    }

    public static string GetPreferredBlobPath(string filesDirectory, string hash, string gamePath)
    {
        var blobId = RequireBlobId(hash);
        var ext = GetPreferredExtensionFromGamePath(gamePath);
        return Path.Combine(filesDirectory, blobId + ext);
    }

    public static string? FindAnyExistingBlobPath(string filesDirectory, string hash)
    {
        var blobId = RequireBlobId(hash);
        if (!Directory.Exists(filesDirectory))
            return null;

        // Old snapshots stored blobs without an extension. Keep those snapshots loadable.
        var extensionlessPath = Path.Combine(filesDirectory, blobId);
        if (File.Exists(extensionlessPath))
            return extensionlessPath;

        return Directory.EnumerateFiles(filesDirectory, blobId + ".*").FirstOrDefault();
    }

    public static IReadOnlyList<string> FindAllExistingBlobPaths(string filesDirectory, string hash)
    {
        var blobId = RequireBlobId(hash);
        if (!Directory.Exists(filesDirectory))
            return [];

        var fullFilesDirectory = Path.GetFullPath(filesDirectory);
        if ((File.GetAttributes(fullFilesDirectory) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Snapshot file cleanup will not follow a reparse-point files directory.");

        var paths = new List<string>();
        foreach (var path in Directory.EnumerateFiles(fullFilesDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(path);
            if (!IsManagedBlobFileName(fileName, blobId))
                continue;

            var fullPath = Path.GetFullPath(path);
            if (!string.Equals(Path.GetDirectoryName(fullPath), fullFilesDirectory,
                    StringComparison.OrdinalIgnoreCase))
                continue;

            paths.Add(fullPath);
        }

        return paths;
    }

    public static string ResolveBlobPath(string filesDirectory, string hash, string gamePath)
    {
        var preferredPath = GetPreferredBlobPath(filesDirectory, hash, gamePath);
        if (File.Exists(preferredPath))
            return preferredPath;

        var anyExisting = FindAnyExistingBlobPath(filesDirectory, hash);
        return anyExisting ?? preferredPath;
    }

    private static string RequireBlobId(string value)
    {
        if (TryNormalizeBlobId(value, out var blobId))
            return blobId;

        throw new InvalidDataException("Snapshot blob identifier is not a supported content hash.");
    }

    private static bool IsManagedBlobFileName(string fileName, string blobId)
    {
        if (string.Equals(fileName, blobId, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!fileName.StartsWith(blobId + ".", StringComparison.OrdinalIgnoreCase))
            return false;

        var extension = fileName[(blobId.Length + 1)..];
        return extension.Length is > 0 and <= 15 && !extension.Contains('.');
    }

    private static string NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return Constants.DataFileExtension;

        var ext = extension.Trim();
        if (!ext.StartsWith(".", StringComparison.Ordinal))
            ext = "." + ext;

        if (ext.Length > 16)
            return Constants.DataFileExtension;

        if (ext.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return Constants.DataFileExtension;

        return ext.ToLowerInvariant();
    }
}
