using ECommons.Reflection;

namespace Snappy.Integrations;

public sealed partial class MareIpc
{
    public string? GetFileCachePath(string hash)
    {
        RefreshPluginAvailability();

        var availablePlugins = _marePlugins.Values.Where(p => p.IsAvailable).ToList();
        if (!availablePlugins.Any()) return null;

        InitializeAllPlugins();

        foreach (var marePlugin in availablePlugins)
        {
            if (marePlugin.FileCacheManager == null || marePlugin.GetFileCacheByHashMethod == null) continue;

            try
            {
                object? fileCacheEntityObject;
                var methodParams = marePlugin.GetFileCacheByHashMethod.GetParameters();

                if (methodParams.Length == 2 && methodParams[0].ParameterType == typeof(string) &&
                    methodParams[1].ParameterType == typeof(bool))
                {
                    var parameters = new object[] { hash, false };
                    fileCacheEntityObject =
                        marePlugin.GetFileCacheByHashMethod.Invoke(marePlugin.FileCacheManager, parameters);
                }
                else if (methodParams.Length == 1 && methodParams[0].ParameterType == typeof(string))
                {
                    fileCacheEntityObject =
                        marePlugin.GetFileCacheByHashMethod.Invoke(marePlugin.FileCacheManager, new object[] { hash });
                }
                else
                {
                    PluginLog.Warning(
                        $"[Mare IPC] Method GetFileCacheByHash for {marePlugin.PluginName} has an unexpected signature.");
                    continue;
                }

                var filePath = fileCacheEntityObject?.GetFoP("ResolvedFilepath") as string;
                if (!string.IsNullOrEmpty(filePath))
                {
                    PluginLog.Debug($"Found file cache path from {marePlugin.PluginName}: {filePath}");
                    return filePath;
                }
            }
            catch (Exception e)
            {
                PluginLog.Error(
                    $"An exception occurred while reflecting into {marePlugin.PluginName} for file cache path.\n{e}");
            }
        }

        return null;
    }
}
