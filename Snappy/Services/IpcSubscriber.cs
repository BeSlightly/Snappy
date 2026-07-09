using System.Diagnostics.CodeAnalysis;
using ECommons.EzIpcManager;
using ECommons.Reflection;

namespace Snappy.Services;

public abstract class IpcSubscriber
{
    protected readonly string _identifier;
    protected bool _wasAvailable;

    public IpcSubscriber(string identifier)
    {
        _identifier = identifier;
        EzIPC.Init(this, identifier, SafeWrapper.AnyException);
        _wasAvailable = IsPluginLoaded();
    }

    protected bool IsPluginLoaded()
    {
        return IsPluginLoaded(_identifier);
    }

    protected static bool IsPluginLoaded(string identifier)
    {
        var installedPlugin = Svc.PluginInterface.InstalledPlugins
            .Where(plugin => string.Equals(plugin.InternalName, identifier, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(plugin => plugin.IsLoaded)
            .FirstOrDefault();

        if (installedPlugin != null)
            return installedPlugin.IsLoaded;

        return DalamudReflector.TryGetDalamudPlugin(identifier, out _, true, true);
    }

    protected static bool TryGetLoadedPluginInstance(string identifier, [NotNullWhen(true)] out object? plugin)
    {
        plugin = null;

        if (!IsPluginLoaded(identifier))
            return false;

        if (!DalamudReflector.TryGetDalamudPlugin(identifier, out var loadedPlugin, true, true))
            return false;

        plugin = loadedPlugin;
        return true;
    }

    public virtual bool IsReady()
    {
        return IsPluginLoaded();
    }


    /// <summary>
    ///     Called when a plugin list change event is received
    /// </summary>
    public virtual void HandlePluginListChanged(IEnumerable<string> affectedPluginNames)
    {
        // Check if our target plugin was affected
        if (affectedPluginNames.Contains(_identifier))
        {
            var isAvailable = IsReady();
            if (isAvailable != _wasAvailable)
            {
                PluginLog.Information(
                    $"[{_identifier} IPC] Plugin state changed via plugin list event: {_wasAvailable} -> {isAvailable}");
                OnPluginStateChanged(isAvailable, _wasAvailable);
                _wasAvailable = isAvailable;
            }
        }
    }

    /// <summary>
    ///     Called when the plugin's availability state changes
    ///     Override this to reset cached data when plugins are reloaded
    /// </summary>
    protected virtual void OnPluginStateChanged(bool isAvailable, bool wasAvailable)
    {
        // Default implementation does nothing
    }
}
