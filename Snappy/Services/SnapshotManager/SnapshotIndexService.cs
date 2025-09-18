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

                    if (sourceActorToken is { Type: JTokenType.String })
                    {
                        var actorName = sourceActorToken.Value<string>();
                        if (!string.IsNullOrEmpty(actorName)) _snapshotIndex[actorName] = dir;
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
        _snapshotIndex.TryGetValue(character.Name.TextValue, out var path);
        return path;
    }
}