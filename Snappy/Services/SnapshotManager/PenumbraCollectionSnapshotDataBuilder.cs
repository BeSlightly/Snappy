namespace Snappy.Services.SnapshotManager;

public sealed class PenumbraCollectionSnapshotDataBuilder
{
    private readonly IIpcManager _ipcManager;

    public PenumbraCollectionSnapshotDataBuilder(IIpcManager ipcManager)
    {
        _ipcManager = ipcManager;
    }

    public SnapshotData Build(SnapshotLiveState state)
    {
        PluginLog.Debug($"Building snapshot from Penumbra collection cache for: {state.CharacterName}");

        var collectionFiles = _ipcManager.PenumbraGetCollectionResolvedFiles(state.ObjectIndex);
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
