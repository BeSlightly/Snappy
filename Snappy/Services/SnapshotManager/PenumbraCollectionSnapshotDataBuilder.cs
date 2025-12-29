using Snappy.Common.Utilities;

namespace Snappy.Services.SnapshotManager;

public sealed class PenumbraCollectionSnapshotDataBuilder
{
    private readonly IIpcManager _ipcManager;

    public PenumbraCollectionSnapshotDataBuilder(IIpcManager ipcManager)
    {
        _ipcManager = ipcManager;
    }

    public async Task<SnapshotData?> BuildAsync(ICharacter character)
    {
        PluginLog.Debug($"Building snapshot from Penumbra collection cache for: {character.Name.TextValue}");
        var newGlamourer = _ipcManager.GetGlamourerState(character);
        var newCustomize = _ipcManager.GetCustomizePlusScale(character);
        var newManipulation = _ipcManager.GetMetaManipulations(character.ObjectIndex);

        var collectionFiles = _ipcManager.PenumbraGetCollectionResolvedFiles(character.ObjectIndex);
        var newFileReplacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var resolvedPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var hashCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (gamePath, filePath) in collectionFiles)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                continue;

            if (!hashCache.TryGetValue(filePath, out var hash))
            {
                hash = PluginUtil.GetFileHash(filePath);
                hashCache[filePath] = hash;
                resolvedPaths[hash] = filePath;
            }

            newFileReplacements[gamePath] = hash;
        }

        return new SnapshotData(newGlamourer, newCustomize, newManipulation, newFileReplacements, resolvedPaths);
    }
}
