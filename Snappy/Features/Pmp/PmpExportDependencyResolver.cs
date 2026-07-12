namespace Snappy.Features.Pmp;

public static class PmpExportDependencyResolver
{
    public static HashSet<string> ExpandDependencies(
        IReadOnlySet<string> selectedPaths,
        IReadOnlyDictionary<string, string> fileMap,
        string filesDirectory,
        IEnumerable<string>? additionalGamePaths = null)
    {
        var expandedPaths = new HashSet<string>(selectedPaths, StringComparer.OrdinalIgnoreCase);
        if (selectedPaths.Count == 0 || fileMap.Count == 0)
            return expandedPaths;

        var graph = PmpFileDependencyGraph.Build(fileMap, filesDirectory, additionalGamePaths);
        var expandedNormalized = graph.ExpandDependencies(selectedPaths);
        foreach (var gamePath in fileMap.Keys)
            if (expandedNormalized.Contains(PmpFileDependencyGraph.NormalizeGamePath(gamePath)))
                expandedPaths.Add(gamePath);

        return expandedPaths;
    }
}
