using Dalamud.Game.ClientState.Objects.SubKinds;
using Newtonsoft.Json.Linq;
using Snappy.Common;

namespace Snappy.Services.SnapshotManager;

public class SnapshotIndexService : ISnapshotIndexService
{
    private readonly Configuration _configuration;
    private readonly Dictionary<string, string> _snapshotIndex = new(StringComparer.OrdinalIgnoreCase);

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

    private static string BuildWorldIdKey(string baseName, int worldId)
    {
        return $"{baseName}@{worldId}";
    }

    private static string BuildWorldNameKey(string baseName, string worldName)
    {
        return $"{baseName}@{worldName}";
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
                    var jsonContent = File.ReadAllText(snapshotJsonPath);
                    var jObject = JObject.Parse(jsonContent);
                    var sourceActorToken = jObject["SourceActor"];
                    var sourceWorldIdToken = jObject["SourceWorldId"];
                    var sourceWorldNameToken = jObject["SourceWorldName"];

                    if (sourceActorToken is { Type: JTokenType.String })
                    {
                        var actorName = sourceActorToken.Value<string>();
                        if (string.IsNullOrWhiteSpace(actorName))
                            continue;

                        var (baseName, actorWorldName) = SplitActorName(actorName);
                        var worldName = sourceWorldNameToken is { Type: JTokenType.String }
                            ? sourceWorldNameToken.Value<string>()
                            : null;
                        if (string.IsNullOrWhiteSpace(worldName))
                            worldName = actorWorldName;

                        int? worldId = null;
                        if (sourceWorldIdToken is { Type: JTokenType.Integer })
                            worldId = sourceWorldIdToken.Value<int>();
                        else if (sourceWorldIdToken is { Type: JTokenType.String }
                                 && int.TryParse(sourceWorldIdToken.Value<string>(), out var parsedWorldId))
                            worldId = parsedWorldId;

                        string key;
                        if (worldId is > 0)
                            key = BuildWorldIdKey(baseName, worldId.Value);
                        else if (!string.IsNullOrWhiteSpace(worldName))
                            key = BuildWorldNameKey(baseName, worldName);
                        else
                            key = actorName;

                        _snapshotIndex[key] = dir;
                    }
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

        PluginLog.Debug($"Snapshot index refreshed. Found {_snapshotIndex.Count} entries.");
    }

    public string? FindSnapshotPathForActor(ICharacter character)
    {
        if (character == null || !character.IsValid())
            return null;

        var actorName = character.Name.TextValue;
        if (TryGetWorldId(character, out var worldId))
        {
            var worldIdKey = BuildWorldIdKey(actorName, worldId);
            if (_snapshotIndex.TryGetValue(worldIdKey, out var path))
                return path;

            if (Snappy.WorldNames.TryGetValue((uint)worldId, out var worldName))
            {
                var worldNameKey = BuildWorldNameKey(actorName, worldName);
                if (_snapshotIndex.TryGetValue(worldNameKey, out path))
                    return path;
            }
        }

        _snapshotIndex.TryGetValue(actorName, out var fallbackPath);
        return fallbackPath;
    }
}
