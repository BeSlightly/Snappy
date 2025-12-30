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
        return DalamudReflector.TryGetDalamudPlugin(_identifier, out _, false, true);
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
