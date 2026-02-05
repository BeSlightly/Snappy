namespace Snappy.UI.Windows;

internal static class MareForkColors
{
    public static readonly Vector4 Snowcloak = new(0.4275f, 0.6863f, 1f, 1f);
    public static readonly Vector4 LightlessSync = new(0.6784f, 0.5412f, 0.9608f, 1f);
    public static readonly Vector4 PlayerSync = new(0.4745f, 0.8392f, 0.7569f, 1f);

    public static bool TryGetByPluginName(string pluginName, out Vector4 color)
    {
        switch (pluginName)
        {
            case "Snowcloak":
                color = Snowcloak;
                return true;
            case "LightlessSync":
                color = LightlessSync;
                return true;
            case "MareSempiterne":
                color = PlayerSync;
                return true;
            default:
                color = default;
                return false;
        }
    }
}
