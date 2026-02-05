namespace Snappy.Common.Utilities;

public static class PathSanitizer
{
    public static string SanitizeFileSystemName(string? value, string fallback)
    {
        var sanitized = value ?? string.Empty;
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
            sanitized = sanitized.Replace(invalidChar, '_');

        // Also replace colon specifically (which might not be in GetInvalidFileNameChars on all systems)
        sanitized = sanitized.Replace(':', '_');
        sanitized = sanitized.TrimEnd(' ', '.');

        if (string.IsNullOrWhiteSpace(sanitized))
            sanitized = string.IsNullOrWhiteSpace(fallback) ? "entry" : fallback;

        return sanitized;
    }
}
