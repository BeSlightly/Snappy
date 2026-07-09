namespace Snappy.Services.SnapshotManager;

public record SnapshotLiveState(
    string CharacterName,
    int ObjectIndex,
    string Glamourer,
    string Customize,
    string Manipulation);

public record SnapshotData(
    string Glamourer,
    string Customize,
    string Manipulation,
    Dictionary<string, string> FileReplacements,
    Dictionary<string, string> FileSwaps,
    Dictionary<string, string> ResolvedPaths);
