using System;
using System.Globalization;
using System.Linq;
using Snappy.Models;

namespace Snappy.Common.Utilities;

public static class FileMapUtil
{
    private const int MaxMapDepth = 64;

    public static Dictionary<string, string> ResolveFileMap(SnapshotInfo snapshotInfo, string? fileMapId)
    {
        var resolved = ResolveInternal(snapshotInfo, fileMapId);
        if (resolved.Count > 0) return resolved;

        // Fallback to legacy field for backward compatibility
        return new Dictionary<string, string>(snapshotInfo.FileReplacements, StringComparer.OrdinalIgnoreCase);
    }

    public static Dictionary<string, string> CalculateChanges(
        IReadOnlyDictionary<string, string> currentMap,
        IReadOnlyDictionary<string, string> incoming)
    {
        var changes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (gamePath, hash) in incoming)
            if (!currentMap.TryGetValue(gamePath, out var existing) ||
                !string.Equals(existing, hash, StringComparison.OrdinalIgnoreCase))
                changes[gamePath] = hash;

        return changes;
    }

    public static string CreateBaseMapIfMissing(SnapshotInfo snapshotInfo, IDictionary<string, string> baseMap, DateTime now)
    {
        if (snapshotInfo.CurrentFileMapId != null || !baseMap.Any())
            return snapshotInfo.CurrentFileMapId ?? string.Empty;

        var baseId = Guid.NewGuid().ToString("N");
        snapshotInfo.FileMaps.Add(new FileMapEntry
        {
            Id = baseId,
            BaseId = null,
            Changes = new Dictionary<string, string>(baseMap, StringComparer.OrdinalIgnoreCase),
            Timestamp = now.ToString("o", CultureInfo.InvariantCulture)
        });
        snapshotInfo.CurrentFileMapId = baseId;
        return baseId;
    }

    public static Dictionary<string, string> ApplyChanges(
        IReadOnlyDictionary<string, string> baseMap,
        IReadOnlyDictionary<string, string> changes)
    {
        var result = new Dictionary<string, string>(baseMap, StringComparer.OrdinalIgnoreCase);
        foreach (var (gamePath, hash) in changes) result[gamePath] = hash;
        return result;
    }

    private static Dictionary<string, string> ResolveInternal(SnapshotInfo snapshotInfo, string? fileMapId)
    {
        if (snapshotInfo.FileMaps == null || snapshotInfo.FileMaps.Count == 0 || string.IsNullOrEmpty(fileMapId))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var mapIndex = snapshotInfo.FileMaps.ToDictionary(m => m.Id, m => m, StringComparer.OrdinalIgnoreCase);
        if (!mapIndex.TryGetValue(fileMapId, out var entry))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return ResolveEntry(entry, mapIndex, 0);
    }

    private static Dictionary<string, string> ResolveEntry(
        FileMapEntry entry,
        IReadOnlyDictionary<string, FileMapEntry> mapIndex,
        int depth)
    {
        if (depth > MaxMapDepth)
            throw new InvalidOperationException("File map resolution exceeded max depth. Possible cycle in FileMaps.");

        Dictionary<string, string> baseMap = new(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(entry.BaseId) && mapIndex.TryGetValue(entry.BaseId, out var baseEntry))
            baseMap = ResolveEntry(baseEntry, mapIndex, depth + 1);

        return ApplyChanges(baseMap, entry.Changes);
    }
}
