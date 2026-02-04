using Dalamud.Utility;

namespace Snappy.Common.Utilities;

public static class JsonUtil
{
    public static void Serialize(object? obj, string filePath)
    {
        if (obj == null) return;
        try
        {
            var json = JsonConvert.SerializeObject(obj, Formatting.Indented);
            var dir = Path.GetDirectoryName(filePath);
            if (dir != null) Directory.CreateDirectory(dir);

            FilesystemUtil.WriteAllTextSafe(filePath, json);
        }
        catch (Exception ex)
        {
            PluginLog.Error($"Failed to serialize {obj.GetType().Name} to {filePath}: {ex}");
        }
    }

    public static async Task<T?> DeserializeAsync<T>(string filePath) where T : class
    {
        if (!File.Exists(filePath)) return null;

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            return JsonConvert.DeserializeObject<T>(json);
        }
        catch (Exception ex)
        {
            PluginLog.Error($"Failed to deserialize {typeof(T).Name} from {filePath}: {ex}");
            return null;
        }
    }

    public static async Task<T?> DeserializeAsync<T>(string filePath, JsonSerializerSettings settings)
        where T : class
    {
        if (!File.Exists(filePath)) return null;

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            return JsonConvert.DeserializeObject<T>(json, settings);
        }
        catch (Exception ex)
        {
            PluginLog.Error($"Failed to deserialize {typeof(T).Name} from {filePath}: {ex}");
            return null;
        }
    }
}
