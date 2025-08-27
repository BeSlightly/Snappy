using System;
using System.Collections;
using System.Linq;
using ECommons.Reflection;
using Snappy.Utils;

namespace Snappy.Bypass;

/// <summary>
/// Bypasses Snowcloak's NoSnapService using direct plugin manipulation.
/// </summary>
public static class NoSnapBypass
{
    public static bool IsBypassActive { get; private set; }

    /// <summary>
    /// Attempts to bypass the NoSnapService by directly manipulating the Snowcloak plugin.
    /// </summary>
    public static bool TryBypassNoSnapService()
    {
        try
        {
            Logger.Info("[NoSnapBypass] Attempting bypass of Snowcloak's NoSnapService...");

            if (!DalamudReflector.TryGetDalamudPlugin("Snowcloak", out var pluginInstance, true))
            {
                Logger.Debug("[NoSnapBypass] Snowcloak not found - bypass not needed");
                return IsBypassActive = true;
            }

            var serviceType = pluginInstance.GetType().Assembly.GetTypes().FirstOrDefault(t => t.Name == "NoSnapService");
            var services = pluginInstance.GetFoP("_host")?.GetFoP("Services");
            var noSnapService = serviceType == null || services == null ? null :
                (services as IEnumerable)?.Cast<object>().FirstOrDefault(s => serviceType.IsAssignableFrom(s?.GetType())) ??
                services.GetType().GetMethod("GetService", new[] { typeof(Type) })?.Invoke(services, new object[] { serviceType });

            if (noSnapService?.GetFoP<System.Collections.Generic.Dictionary<string, bool>>("_listOfPlugins") is var listOfPlugins && listOfPlugins != null)
            {
                listOfPlugins["Snappy"] = false;
                noSnapService.Call("Update", null, Array.Empty<object>());
                Logger.Info("[NoSnapBypass] Successfully disabled Snappy monitoring");
                return IsBypassActive = true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Logger.Error("[NoSnapBypass] Critical error during bypass attempt", ex);
            return false;
        }
    }

    public static void DisableBypass()
    {
        IsBypassActive = false;
        Logger.Info("[NoSnapBypass] Bypass disabled successfully");
    }
}