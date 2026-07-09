namespace Snappy.Services.SnapshotManager;

public sealed class LiveSnapshotDataBuilder
{
    private readonly IIpcManager _ipcManager;

    public LiveSnapshotDataBuilder(IIpcManager ipcManager)
    {
        _ipcManager = ipcManager;
    }

    public Task<SnapshotData?> BuildAsync(ICharacter character)
    {
        PluginLog.Debug($"Building snapshot from live data for: {character.Name.TextValue}");
        var newGlamourer = _ipcManager.GetGlamourerState(character);
        var newCustomize = _ipcManager.GetCustomizePlusScale(character);
        var newManipulation = _ipcManager.GetMetaManipulations(character.ObjectIndex);
        var newFileReplacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var newFileSwaps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var resolvedPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var hashCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var penumbraReplacements = _ipcManager.PenumbraGetGameObjectResourcePaths(character.ObjectIndex);

        foreach (var (resolvedPath, gamePaths) in penumbraReplacements)
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

        return Task.FromResult<SnapshotData?>(new SnapshotData(newGlamourer, newCustomize, newManipulation,
            newFileReplacements, newFileSwaps, resolvedPaths));
    }
}
