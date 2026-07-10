namespace Snappy.Common.Utilities;

public static class GamePathUtil
{
    public static string Normalize(string? gamePath)
    {
        if (string.IsNullOrWhiteSpace(gamePath))
            return string.Empty;

        var segments = gamePath.Replace('\\', '/').Trim().TrimStart('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0
            || segments.Any(segment => segment is "." or ".."
                                       || segment.Contains(':')
                                       || segment.Any(char.IsControl)))
            return string.Empty;

        return string.Join('/', segments);
    }

    public static Dictionary<string, string> NormalizeFileMap(
        IEnumerable<KeyValuePair<string, string>> mappings)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rawGamePath, rawHash) in mappings)
        {
            var gamePath = Normalize(rawGamePath);
            if (!string.IsNullOrEmpty(gamePath)
                && SnapshotBlobUtil.TryNormalizeBlobId(rawHash, out var hash))
                result[gamePath] = hash;
        }

        return result;
    }

    public static Dictionary<string, string> NormalizeFileSwaps(
        IEnumerable<KeyValuePair<string, string>> mappings)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rawGamePath, rawSwapPath) in mappings)
        {
            var gamePath = Normalize(rawGamePath);
            var swapPath = Normalize(rawSwapPath);
            if (!string.IsNullOrEmpty(gamePath) && !string.IsNullOrEmpty(swapPath))
                result[gamePath] = swapPath;
        }

        return result;
    }
}
