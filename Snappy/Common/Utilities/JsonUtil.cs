using Dalamud.Utility;

namespace Snappy.Common.Utilities;

public static class JsonUtil
{
    private sealed class StagedJsonWrite(string outputPath, string temporaryPath, bool hadOriginal)
    {
        public string OutputPath { get; } = outputPath;
        public string TemporaryPath { get; } = temporaryPath;
        public bool HadOriginal { get; } = hadOriginal;
        public string? BackupPath { get; set; }
        public bool Committed { get; set; }
    }

    public static bool Serialize(object? obj, string filePath)
    {
        if (obj == null)
            return false;

        try
        {
            var json = JsonConvert.SerializeObject(obj, Formatting.Indented);
            var dir = Path.GetDirectoryName(filePath);
            if (dir != null) Directory.CreateDirectory(dir);

            FilesystemUtil.WriteAllTextSafe(filePath, json);
            return true;
        }
        catch (Exception ex)
        {
            PluginLog.Error($"Failed to serialize {obj.GetType().Name} to {filePath}: {ex}");
            return false;
        }
    }

    public static bool SerializeAll(params (object? Value, string FilePath)[] writes)
    {
        if (writes.Length == 0)
            return true;

        var stagedWrites = new List<StagedJsonWrite>(writes.Length);
        try
        {
            var outputPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (value, filePath) in writes)
            {
                if (value == null)
                    throw new ArgumentNullException(nameof(writes), "A JSON transaction value was null.");

                var outputPath = Path.GetFullPath(filePath);
                if (!outputPaths.Add(outputPath))
                    throw new InvalidOperationException($"JSON transaction contains duplicate path '{outputPath}'.");

                var json = JsonConvert.SerializeObject(value, Formatting.Indented);
                var temporaryPath = AtomicFileUtil.CreateTemporaryOutputPath(outputPath);
                var stagedWrite = new StagedJsonWrite(outputPath, temporaryPath, File.Exists(outputPath));
                stagedWrites.Add(stagedWrite);
                File.WriteAllText(temporaryPath, json);
            }

            foreach (var write in stagedWrites)
            {
                if (write.HadOriginal)
                {
                    write.BackupPath = AtomicFileUtil.CreateTemporaryOutputPath(write.OutputPath);
                    File.Replace(write.TemporaryPath, write.OutputPath, write.BackupPath, true);
                }
                else
                {
                    File.Move(write.TemporaryPath, write.OutputPath);
                }

                write.Committed = true;
            }

            foreach (var write in stagedWrites)
                if (write.BackupPath != null)
                    AtomicFileUtil.TryDelete(write.BackupPath);

            return true;
        }
        catch (Exception ex)
        {
            RollBackWrites(stagedWrites);
            PluginLog.Error($"Failed to serialize JSON state transaction: {ex}");
            return false;
        }
        finally
        {
            foreach (var write in stagedWrites)
                AtomicFileUtil.TryDelete(write.TemporaryPath);
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

    public static async Task<T?> DeserializeStateAsync<T>(string filePath) where T : class
    {
        if (!File.Exists(filePath))
            return null;

        return await DeserializeAsync<T>(filePath)
               ?? throw new InvalidDataException($"Existing state file '{filePath}' could not be read.");
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

    private static void RollBackWrites(IEnumerable<StagedJsonWrite> stagedWrites)
    {
        foreach (var write in stagedWrites.Reverse())
        {
            try
            {
                if (write.BackupPath != null && File.Exists(write.BackupPath))
                {
                    if (File.Exists(write.OutputPath))
                        File.Replace(write.BackupPath, write.OutputPath, null, true);
                    else
                        File.Move(write.BackupPath, write.OutputPath);
                }
                else if (write.Committed && !write.HadOriginal)
                {
                    File.Delete(write.OutputPath);
                }
            }
            catch (Exception rollbackException)
            {
                PluginLog.Error(
                    $"Failed to roll back JSON state file '{write.OutputPath}'. Backup retained at '{write.BackupPath}': {rollbackException}");
            }
        }
    }
}
