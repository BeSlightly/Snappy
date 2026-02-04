using Dalamud.Game.ClientState.Objects.SubKinds;
using ECommons.ExcelServices;
using Snappy.Common;

namespace Snappy.Services.SnapshotManager;

public class SnapshotIndexService : ISnapshotIndexService
{
    private readonly Configuration _configuration;
    private readonly Dictionary<string, List<SnapshotIndexEntry>> _snapshotIndex =
        new(StringComparer.OrdinalIgnoreCase);

    private sealed record SnapshotIndexEntry(string Path, int? WorldId, string? WorldName, string SourceActor);

    private static readonly JsonSerializerSettings SnapshotIndexSettings = new()
    {
        Error = (_, args) =>
        {
            // Ignore per-property errors so legacy snapshot formats can still be indexed.
            args.ErrorContext.Handled = true;
        }
    };

    public SnapshotIndexService(Configuration configuration)
    {
        _configuration = configuration;
    }

    private static (string BaseName, string? WorldName) SplitActorName(string actorName)
    {
        var atIndex = actorName.IndexOf('@');
        if (atIndex <= 0 || atIndex == actorName.Length - 1)
            return (actorName, null);

        var baseName = actorName[..atIndex];
        var worldName = actorName[(atIndex + 1)..];
        return (baseName, string.IsNullOrWhiteSpace(worldName) ? null : worldName);
    }

    private static bool TryGetWorldId(ICharacter character, out int worldId)
    {
        worldId = 0;
        if (character is IPlayerCharacter playerCharacter)
        {
            worldId = (int)playerCharacter.HomeWorld.RowId;
            return worldId > 0;
        }

        return false;
    }

    public void RefreshSnapshotIndex()
    {
        _snapshotIndex.Clear();
        PluginLog.Debug("Refreshing snapshot index...");

        var workingDir = _configuration.WorkingDirectory;
        if (string.IsNullOrEmpty(workingDir) || !Directory.Exists(workingDir))
        {
            PluginLog.Warning("Working directory not set or not found. Snapshot index will be empty.");
            return;
        }

        try
        {
            var snapshotDirs = Directory.GetDirectories(workingDir);
            foreach (var dir in snapshotDirs)
            {
                var snapshotJsonPath = SnapshotPaths.From(dir).SnapshotFile;
                if (!File.Exists(snapshotJsonPath))
                    continue;

                try
                {
                    var snapshotInfo = JsonUtil
                        .DeserializeAsync<SnapshotInfo>(snapshotJsonPath, SnapshotIndexSettings)
                        .GetAwaiter()
                        .GetResult();
                    if (snapshotInfo == null)
                        continue;

                    var actorName = snapshotInfo.SourceActor;
                    if (string.IsNullOrWhiteSpace(actorName))
                        continue;

                    var (baseName, actorWorldName) = SplitActorName(actorName);
                    var worldName = string.IsNullOrWhiteSpace(snapshotInfo.SourceWorldName)
                        ? actorWorldName
                        : snapshotInfo.SourceWorldName;
                    var worldId = snapshotInfo.SourceWorldId;

                    var entry = new SnapshotIndexEntry(dir, worldId, worldName, actorName);
                    if (!_snapshotIndex.TryGetValue(baseName, out var entries))
                    {
                        entries = new List<SnapshotIndexEntry>();
                        _snapshotIndex[baseName] = entries;
                    }

                    entries.Add(entry);
                }
                catch (Exception ex)
                {
                    PluginLog.Warning(
                        $"Could not read or parse snapshot.json in '{Path.GetFileName(dir)}' during index refresh. Skipping. Error: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            PluginLog.Error($"An error occurred while building snapshot index: {ex.Message}");
        }

        var totalEntries = _snapshotIndex.Values.Sum(list => list.Count);
        PluginLog.Debug(
            $"Snapshot index refreshed. Found {totalEntries} snapshots across {_snapshotIndex.Count} actor keys.");
    }

    public string? FindSnapshotPathForActor(ICharacter character)
    {
        if (character == null || !character.IsValid())
            return null;

        var actorName = character.Name.TextValue;
        var (baseName, _) = SplitActorName(actorName);
        if (!_snapshotIndex.TryGetValue(baseName, out var entries) || entries.Count == 0)
            return null;

        if (TryGetWorldId(character, out var worldId))
        {
            var byWorldId = entries.FirstOrDefault(e => e.WorldId == worldId);
            if (byWorldId != null)
                return byWorldId.Path;

            var worldName = ExcelWorldHelper.GetName((uint)worldId);
            if (!string.IsNullOrWhiteSpace(worldName))
            {
                var byWorldName = entries.FirstOrDefault(e =>
                    string.Equals(e.WorldName, worldName, StringComparison.OrdinalIgnoreCase));
                if (byWorldName != null)
                    return byWorldName.Path;
            }
        }

        return entries.Count == 1 ? entries[0].Path : null;
    }
}
