namespace Snappy.Services.SnapshotManager;

public sealed class LiveSnapshotDataBuilder
{
    public SnapshotData Build(SnapshotLiveState state, IReadOnlyDictionary<string, HashSet<string>> resourcePaths)
    {
        PluginLog.Debug($"Building snapshot from live data for: {state.CharacterName}");
        var newFileReplacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var newFileSwaps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var resolvedPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var hashCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (resolvedPath, gamePaths) in resourcePaths)
        {
            if (!File.Exists(resolvedPath))
            {
                if (!Path.IsPathRooted(resolvedPath))
                    foreach (var gamePath in gamePaths)
                        if (!string.Equals(gamePath, resolvedPath, StringComparison.OrdinalIgnoreCase))
                            newFileSwaps[gamePath] = resolvedPath;
                continue;
            }

            if (!hashCache.TryGetValue(resolvedPath, out var hash))
            {
                hash = PluginUtil.GetFileHash(resolvedPath);
                hashCache[resolvedPath] = hash;
                resolvedPaths[hash] = resolvedPath;
            }

            foreach (var gamePath in gamePaths) newFileReplacements[gamePath] = hash;
        }

        return new SnapshotData(state.Glamourer, state.Customize, state.Manipulation, newFileReplacements,
            newFileSwaps, resolvedPaths);
    }
}
