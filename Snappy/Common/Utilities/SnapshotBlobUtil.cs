using System;
using System.IO;
using System.Linq;
using Snappy.Common;

namespace Snappy.Common.Utilities;

public static class SnapshotBlobUtil
{
    public static string GetPreferredExtensionFromGamePath(string gamePath)
    {
        var ext = Path.GetExtension(gamePath);
        return NormalizeExtension(ext);
    }

    public static string GetPreferredBlobPath(string filesDirectory, string hash, string gamePath)
    {
        var ext = GetPreferredExtensionFromGamePath(gamePath);
        return Path.Combine(filesDirectory, hash + ext);
    }

    public static string? FindAnyExistingBlobPath(string filesDirectory, string hash)
    {
        if (!Directory.Exists(filesDirectory))
            return null;

        return Directory.EnumerateFiles(filesDirectory, hash + ".*").FirstOrDefault();
    }

    public static string ResolveBlobPath(string filesDirectory, string hash, string gamePath)
    {
        var preferredPath = GetPreferredBlobPath(filesDirectory, hash, gamePath);
        if (File.Exists(preferredPath))
            return preferredPath;

        var anyExisting = FindAnyExistingBlobPath(filesDirectory, hash);
        return anyExisting ?? preferredPath;
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
