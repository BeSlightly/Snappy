namespace Snappy.Services.SnapshotManager;

public record SnapshotLiveState(
    string CharacterName,
    string Glamourer,
    string Customize,
    string Manipulation);

public record PenumbraPathState(
    Dictionary<string, HashSet<string>> ResourcePaths,
    Dictionary<string, string> CollectionFiles);

public record SnapshotData(
    string Glamourer,
    string Customize,
    string Manipulation,
    Dictionary<string, string> FileReplacements,
    Dictionary<string, string> FileSwaps,
    Dictionary<string, string> ResolvedPaths);
