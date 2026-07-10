namespace Snappy.Common.Utilities;

public static class GamePathUtil
{
    public static string Normalize(string? gamePath)
    {
        if (string.IsNullOrWhiteSpace(gamePath))
            return string.Empty;

        var normalized = gamePath.Replace('\\', '/').Trim().TrimStart('/');
        return normalized.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment is "." or "..")
            ? string.Empty
            : normalized;
    }

    public static Dictionary<string, string> NormalizeFileMap(
        IEnumerable<KeyValuePair<string, string>> mappings)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rawGamePath, rawHash) in mappings)
        {
            var gamePath = Normalize(rawGamePath);
            var hash = rawHash?.Trim();
            if (!string.IsNullOrEmpty(gamePath) && !string.IsNullOrEmpty(hash))
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
