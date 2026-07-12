using Penumbra.GameData.Data;
using Penumbra.GameData.Files;

namespace Snappy.Features.Pmp;

public sealed class PmpFileDependencyGraph
{
    private readonly Dictionary<string, HashSet<string>> _dependencies =
        new(StringComparer.OrdinalIgnoreCase);

    private PmpFileDependencyGraph()
    {
    }

    public static PmpFileDependencyGraph Build(IReadOnlyDictionary<string, string>? fileMap,
        string? filesDirectory, IEnumerable<string>? additionalGamePaths = null)
    {
        var graph = new PmpFileDependencyGraph();
        if ((fileMap == null || fileMap.Count == 0) && additionalGamePaths == null)
            return graph;
        if (string.IsNullOrWhiteSpace(filesDirectory)
            || !Directory.Exists(filesDirectory))
            return graph;

        var files = (fileMap ?? new Dictionary<string, string>())
            .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Key) && !string.IsNullOrWhiteSpace(kvp.Value))
            .Select(kvp => new FileEntry(kvp.Key, NormalizeGamePath(kvp.Key), kvp.Value))
            .GroupBy(entry => entry.NormalizedPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToDictionary(entry => entry.NormalizedPath, StringComparer.OrdinalIgnoreCase);
        if (additionalGamePaths != null)
        {
            foreach (var gamePath in additionalGamePaths)
            {
                if (string.IsNullOrWhiteSpace(gamePath))
                    continue;

                var normalized = NormalizeGamePath(gamePath);
                files.TryAdd(normalized, new FileEntry(gamePath, normalized, null));
            }
        }

        var byFileName = files.Values
            .GroupBy(entry => Path.GetFileName(entry.NormalizedPath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        foreach (var entry in files.Values)
        {
            if (string.IsNullOrWhiteSpace(entry.Hash))
                continue;

            var blobPath = SnapshotBlobUtil.ResolveBlobPath(filesDirectory, entry.Hash, entry.OriginalPath);
            if (!File.Exists(blobPath))
                continue;

            try
            {
                switch (Path.GetExtension(entry.NormalizedPath).ToLowerInvariant())
                {
                    case ".mdl":
                        graph.AddModelDependencies(entry, File.ReadAllBytes(blobPath), files, byFileName);
                        break;
                    case ".mtrl":
                        graph.AddMaterialDependencies(entry, File.ReadAllBytes(blobPath), files);
                        break;
                    case ".avfx":
                        graph.AddVfxDependencies(entry, File.ReadAllBytes(blobPath), files);
                        break;
                }
            }
            catch (Exception ex)
            {
                PluginLog.Verbose($"[PMP] Failed to inspect dependencies for '{entry.OriginalPath}': {ex.Message}");
            }
        }

        return graph;
    }

    public HashSet<string> ExpandDependencies(IEnumerable<string> roots)
    {
        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>();
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;

            var normalized = NormalizeGamePath(root);
            if (expanded.Add(normalized))
                pending.Enqueue(normalized);
        }

        while (pending.TryDequeue(out var path))
        {
            if (!_dependencies.TryGetValue(path, out var dependencies))
                continue;

            foreach (var dependency in dependencies)
                if (expanded.Add(dependency))
                    pending.Enqueue(dependency);
        }

        return expanded;
    }

    private void AddModelDependencies(FileEntry modelEntry, byte[] bytes,
        IReadOnlyDictionary<string, FileEntry> files,
        IReadOnlyDictionary<string, FileEntry[]> byFileName)
    {
        var model = new MdlFile(bytes);
        foreach (var material in model.Materials)
        {
            if (string.IsNullOrWhiteSpace(material))
                continue;

            foreach (var dependency in ResolveModelMaterial(modelEntry.NormalizedPath, material, files, byFileName))
                AddDependency(modelEntry.NormalizedPath, dependency.NormalizedPath);
        }
    }

    private void AddMaterialDependencies(FileEntry materialEntry, byte[] bytes,
        IReadOnlyDictionary<string, FileEntry> files)
    {
        var material = new MtrlFile(bytes);
        var materialDirectory = GetGamePathDirectory(materialEntry.NormalizedPath);
        foreach (var texture in material.Textures)
        {
            if (string.IsNullOrWhiteSpace(texture.Path))
                continue;

            if (GamePaths.Tex.HandleDx11Path(texture, out var dx11Path))
            {
                AddResolvedDependency(materialEntry.NormalizedPath,
                    ResolveRelativeGamePath(materialDirectory, dx11Path), files);
                if (!string.Equals(dx11Path, texture.Path, StringComparison.OrdinalIgnoreCase))
                    AddResolvedDependency(materialEntry.NormalizedPath,
                        ResolveRelativeGamePath(materialDirectory, texture.Path), files);
            }
            else
            {
                AddResolvedDependency(materialEntry.NormalizedPath,
                    ResolveRelativeGamePath(materialDirectory, texture.Path), files);
            }
        }

        if (!string.IsNullOrWhiteSpace(material.ShaderPackage.Name))
            AddResolvedDependency(materialEntry.NormalizedPath,
                GamePaths.Shader(material.ShaderPackage.Name), files);
    }

    private void AddVfxDependencies(FileEntry vfxEntry, byte[] bytes,
        IReadOnlyDictionary<string, FileEntry> files)
    {
        var vfx = new AvfxFile(bytes);
        var vfxDirectory = GetGamePathDirectory(vfxEntry.NormalizedPath);
        foreach (var texture in vfx.Textures)
            AddResolvedDependency(vfxEntry.NormalizedPath,
                ResolveRelativeGamePath(vfxDirectory, texture), files);
    }

    private void AddResolvedDependency(string parent, string dependency,
        IReadOnlyDictionary<string, FileEntry> files)
    {
        var normalized = NormalizeGamePath(dependency);
        if (files.ContainsKey(normalized))
            AddDependency(parent, normalized);
    }

    private void AddDependency(string parent, string dependency)
    {
        if (string.Equals(parent, dependency, StringComparison.OrdinalIgnoreCase))
            return;

        if (!_dependencies.TryGetValue(parent, out var dependencies))
        {
            dependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _dependencies[parent] = dependencies;
        }

        dependencies.Add(dependency);
    }

    private static IEnumerable<FileEntry> ResolveModelMaterial(string modelPath, string materialReference,
        IReadOnlyDictionary<string, FileEntry> files,
        IReadOnlyDictionary<string, FileEntry[]> byFileName)
    {
        var normalizedReference = NormalizeGamePath(materialReference);
        if (files.TryGetValue(normalizedReference, out var exact))
            return [exact];

        var fileName = Path.GetFileName(normalizedReference);
        if (string.IsNullOrEmpty(fileName) || !byFileName.TryGetValue(fileName, out var candidates))
            return [];

        var assetRoot = GetAssetRoot(modelPath);
        var rootedCandidates = candidates
            .Where(candidate => string.Equals(GetAssetRoot(candidate.NormalizedPath), assetRoot,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (rootedCandidates.Length > 0)
            return rootedCandidates;

        return candidates.Length == 1 ? candidates : [];
    }

    private static string ResolveRelativeGamePath(string baseDirectory, string path)
    {
        var normalized = NormalizeGamePath(path);
        if (normalized.StartsWith("--", StringComparison.Ordinal))
            normalized = normalized[2..];
        if (IsRootedGamePath(normalized) || normalized.IndexOf(':') >= 0)
            return normalized;

        var segments = baseDirectory.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
        foreach (var segment in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
                continue;
            if (segment == "..")
            {
                if (segments.Count > 0)
                    segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        return string.Join('/', segments);
    }

    private static string GetAssetRoot(string path)
    {
        var normalized = NormalizeGamePath(path);
        var modelIndex = normalized.IndexOf("/model/", StringComparison.OrdinalIgnoreCase);
        var materialIndex = normalized.IndexOf("/material/", StringComparison.OrdinalIgnoreCase);
        var splitIndex = modelIndex >= 0 && materialIndex >= 0
            ? Math.Min(modelIndex, materialIndex)
            : Math.Max(modelIndex, materialIndex);
        return splitIndex >= 0 ? normalized[..splitIndex] : GetGamePathDirectory(normalized);
    }

    private static string GetGamePathDirectory(string path)
    {
        var normalized = NormalizeGamePath(path);
        var lastSlash = normalized.LastIndexOf('/');
        return lastSlash <= 0 ? string.Empty : normalized[..lastSlash];
    }

    private static bool IsRootedGamePath(string path)
        => path.StartsWith("chara/", StringComparison.OrdinalIgnoreCase)
           || path.StartsWith("vfx/", StringComparison.OrdinalIgnoreCase)
           || path.StartsWith("ui/", StringComparison.OrdinalIgnoreCase)
           || path.StartsWith("common/", StringComparison.OrdinalIgnoreCase)
           || path.StartsWith("bg/", StringComparison.OrdinalIgnoreCase)
           || path.StartsWith("bgcommon/", StringComparison.OrdinalIgnoreCase)
           || path.StartsWith("shader/", StringComparison.OrdinalIgnoreCase);

    public static string NormalizeGamePath(string path)
        => path.Replace('\\', '/').Trim().TrimStart('/');

    private readonly record struct FileEntry(string OriginalPath, string NormalizedPath, string? Hash);
}
