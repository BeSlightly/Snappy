using Snappy.Common.Utilities;

namespace Snappy.Services.SnapshotManager;

public sealed class LiveSnapshotDataBuilder
{
    private readonly IIpcManager _ipcManager;

    public LiveSnapshotDataBuilder(IIpcManager ipcManager)
    {
        _ipcManager = ipcManager;
    }

    public async Task<SnapshotData?> BuildAsync(ICharacter character,
        Dictionary<string, HashSet<string>>? penumbraReplacements)
    {
        PluginLog.Debug($"Building snapshot from live data for: {character.Name.TextValue}");
        var newGlamourer = _ipcManager.GetGlamourerState(character);
        var newCustomize = _ipcManager.GetCustomizePlusScale(character);
        var newManipulation = _ipcManager.GetMetaManipulations(character.ObjectIndex);
        var newFileReplacements = new Dictionary<string, string>();
        var resolvedPaths = new Dictionary<string, string>();

        penumbraReplacements ??= _ipcManager.PenumbraGetGameObjectResourcePaths(character.ObjectIndex);

        foreach (var (resolvedPath, gamePaths) in penumbraReplacements)
        {
            if (!File.Exists(resolvedPath))
                continue;

            var fileBytes = await File.ReadAllBytesAsync(resolvedPath);
            var hash = PluginUtil.GetFileHash(fileBytes);
            resolvedPaths[hash] = resolvedPath;
            foreach (var gamePath in gamePaths) newFileReplacements[gamePath] = hash;
        }

        return new SnapshotData(newGlamourer, newCustomize, newManipulation, newFileReplacements, resolvedPaths);
    }
}
