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
}
