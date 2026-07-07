using Penumbra.GameData.Data;
using Penumbra.GameData.Files;

namespace Snappy.Features.Pmp;

public static class PmpExportDependencyResolver
{
    private readonly record struct FileMapEntry(string OriginalPath, string Hash);

    public static HashSet<string> ExpandMtrlDependencies(
        IReadOnlySet<string> selectedPaths,
        IReadOnlyDictionary<string, string> fileMap,
        string filesDirectory)
    {
        var expanded = new HashSet<string>(selectedPaths, StringComparer.OrdinalIgnoreCase);
        if (selectedPaths.Count == 0 || fileMap.Count == 0)
            return expanded;

        var normalizedMap = BuildNormalizedFileMap(fileMap);
        if (normalizedMap.Count == 0)
            return expanded;

        var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var gamePath in selectedPaths)
        {
            if (string.IsNullOrWhiteSpace(gamePath))
                continue;
            if (!gamePath.EndsWith(".mtrl", StringComparison.OrdinalIgnoreCase))
                continue;

            var normalized = NormalizeGamePath(gamePath);
            if (!processed.Add(normalized))
                continue;

            if (!normalizedMap.TryGetValue(normalized, out var entry))
                continue;
            if (string.IsNullOrWhiteSpace(entry.Hash))
                continue;

            var mtrlPath = SnapshotBlobUtil.ResolveBlobPath(filesDirectory, entry.Hash, entry.OriginalPath);
            if (!File.Exists(mtrlPath))
                continue;

            if (!TryReadMtrlFile(mtrlPath, out var mtrl) || mtrl == null)
                continue;

            foreach (var texturePath in EnumerateTexturePaths(mtrl, entry.OriginalPath))
            {
                var normalizedTexture = NormalizeGamePath(texturePath);
                if (normalizedMap.TryGetValue(normalizedTexture, out var textureEntry))
                    expanded.Add(textureEntry.OriginalPath);
            }
        }

        return expanded;
    }

    private static Dictionary<string, FileMapEntry> BuildNormalizedFileMap(
        IReadOnlyDictionary<string, string> fileMap)
    {
        var result = new Dictionary<string, FileMapEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var (gamePath, hash) in fileMap)
        {
            if (string.IsNullOrWhiteSpace(gamePath))
                continue;

            result[NormalizeGamePath(gamePath)] = new FileMapEntry(gamePath, hash);
        }

        return result;
    }

    private static bool TryReadMtrlFile(string filePath, out MtrlFile? mtrl)
    {
        mtrl = null;
        try
        {
            var bytes = File.ReadAllBytes(filePath);
            mtrl = new MtrlFile(bytes);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<string> EnumerateTexturePaths(MtrlFile mtrl, string materialPath)
    {
        var materialDirectory = GetGamePathDirectory(materialPath);
        foreach (var texture in mtrl.Textures)
        {
            if (string.IsNullOrWhiteSpace(texture.Path))
                continue;

            if (GamePaths.Tex.HandleDx11Path(texture, out var dx11Path))
            {
                if (!string.IsNullOrWhiteSpace(dx11Path))
                    yield return ResolveMaterialTexturePath(materialDirectory, dx11Path);

                if (!string.Equals(dx11Path, texture.Path, StringComparison.OrdinalIgnoreCase))
                    yield return ResolveMaterialTexturePath(materialDirectory, texture.Path);

                continue;
            }

            yield return ResolveMaterialTexturePath(materialDirectory, texture.Path);
        }
    }

    private static string ResolveMaterialTexturePath(string materialDirectory, string texturePath)
    {
        var normalized = NormalizeGamePath(texturePath);
        if (normalized.StartsWith("--", StringComparison.Ordinal))
            normalized = normalized[2..];

        if (IsRootedGamePath(normalized) || normalized.IndexOf(':') >= 0)
            return normalized;

        var segments = materialDirectory.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
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

    private static string NormalizeGamePath(string path)
        => path.Replace('\\', '/').Trim();
}
