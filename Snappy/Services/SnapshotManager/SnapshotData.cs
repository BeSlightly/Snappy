namespace Snappy.Services.SnapshotManager;

public record SnapshotData(
    string Glamourer,
    string Customize,
    string Manipulation,
    Dictionary<string, string> FileReplacements,
    Dictionary<string, string> ResolvedPaths);
