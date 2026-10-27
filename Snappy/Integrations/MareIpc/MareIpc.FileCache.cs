using System.Reflection;
using System.Threading;
using ECommons.Reflection;

namespace Snappy.Integrations;

public sealed partial class MareIpc
{
    private static readonly string[] FileCacheLookupMethodNames =
    [
        "GetFileCacheByHash",
        "GetFirstFileCacheEntityByHash"
    ];

    public string? GetFileCachePath(string hash)
    {
        RefreshPluginAvailability();

        var availablePlugins = _marePlugins.Values.Where(p => p.IsAvailable).ToList();
        if (!availablePlugins.Any()) return null;

        InitializeAllPlugins();

        foreach (var marePlugin in availablePlugins)
        {
            if (marePlugin.FileCacheManager == null || marePlugin.FileCacheLookupMethod == null) continue;

            try
            {
                var fileCacheEntityObject = InvokeFileCacheLookup(marePlugin.FileCacheLookupMethod,
                    marePlugin.FileCacheManager, hash);

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

    private static MethodInfo? FindFileCacheLookupMethod(Type fileCacheManagerType)
    {
        foreach (var methodName in FileCacheLookupMethodNames)
        {
            var method = fileCacheManagerType
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(candidate => string.Equals(candidate.Name, methodName, StringComparison.Ordinal))
                .Where(IsSupportedFileCacheLookup)
                .OrderBy(candidate => candidate.GetParameters().Length)
                .FirstOrDefault();

            if (method != null)
                return method;
        }

        return null;
    }

    private static bool IsSupportedFileCacheLookup(MethodInfo method)
    {
        var parameters = method.GetParameters();
        if (parameters.Length == 0 || parameters[0].ParameterType != typeof(string))
            return false;

        for (var i = 1; i < parameters.Length; i++)
        {
            var parameterType = parameters[i].ParameterType;
            if (parameterType != typeof(bool) && parameterType != typeof(CancellationToken))
                return false;
        }

        return true;
    }

    private static object? InvokeFileCacheLookup(MethodInfo lookupMethod, object fileCacheManager, string hash)
    {
        var result = lookupMethod.Invoke(fileCacheManager, BuildFileCacheLookupArguments(lookupMethod, hash));

        if (result is not Task task)
            return result;

        task.GetAwaiter().GetResult();
        return task.GetType().GetProperty("Result", BindingFlags.Instance | BindingFlags.Public)?.GetValue(task);
    }

    private static object[] BuildFileCacheLookupArguments(MethodInfo lookupMethod, string hash)
    {
        var parameters = lookupMethod.GetParameters();
        var arguments = new object[parameters.Length];
        arguments[0] = hash;

        for (var i = 1; i < parameters.Length; i++)
        {
            var parameter = parameters[i];

            if (parameter.ParameterType == typeof(CancellationToken))
            {
                arguments[i] = CancellationToken.None;
                continue;
            }

            arguments[i] = string.Equals(parameter.Name, "cacheResults", StringComparison.OrdinalIgnoreCase);
        }

        return arguments;
    }
}
