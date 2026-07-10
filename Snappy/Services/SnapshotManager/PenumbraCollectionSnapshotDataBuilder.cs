namespace Snappy.Services.SnapshotManager;

public sealed class PenumbraCollectionSnapshotDataBuilder
{
    public SnapshotData Build(SnapshotLiveState state, IReadOnlyDictionary<string, string> collectionFiles)
    {
        PluginLog.Debug($"Building snapshot from Penumbra collection cache for: {state.CharacterName}");

        var newFileReplacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var newFileSwaps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var resolvedPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var hashCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (gamePath, filePath) in collectionFiles)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                continue;

            if (!File.Exists(filePath))
            {
                if (!Path.IsPathRooted(filePath)
                    && !string.Equals(gamePath, filePath, StringComparison.OrdinalIgnoreCase))
                    newFileSwaps[gamePath] = filePath;
                continue;
            }

            if (!hashCache.TryGetValue(filePath, out var hash))
            {
                hash = PluginUtil.GetFileHash(filePath);
                hashCache[filePath] = hash;
                resolvedPaths[hash] = filePath;
            }

            newFileReplacements[gamePath] = hash;
        }

        return new SnapshotData(state.Glamourer, state.Customize, state.Manipulation, newFileReplacements, newFileSwaps,
            resolvedPaths);
    }
}
