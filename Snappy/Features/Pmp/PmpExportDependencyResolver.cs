using Penumbra.GameData.Data;
using Penumbra.GameData.Files;
using Snappy.Common.Utilities;

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

            foreach (var texturePath in EnumerateTexturePaths(mtrl))
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

    private static IEnumerable<string> EnumerateTexturePaths(MtrlFile mtrl)
    {
        foreach (var texture in mtrl.Textures)
        {
            if (string.IsNullOrWhiteSpace(texture.Path))
                continue;

            if (GamePaths.Tex.HandleDx11Path(texture, out var dx11Path))
            {
                if (!string.IsNullOrWhiteSpace(dx11Path))
                    yield return dx11Path;

                if (!string.Equals(dx11Path, texture.Path, StringComparison.OrdinalIgnoreCase))
                    yield return texture.Path;

                continue;
            }

            yield return texture.Path;
        }
    }

    private static string NormalizeGamePath(string path)
        => path.Replace('\\', '/').Trim();
}
