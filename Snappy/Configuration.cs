using ECommons.Configuration;

namespace Snappy
{
    public class Configuration : IEzConfig
    {
        public int Version { get; set; } = 0;

        public bool EnableCustomTheme { get; set; } = false;

        public bool DisableAutomaticRevert { get; set; } = false;

        public bool AllowOutsideGpose { get; set; } = false;

        public string WorkingDirectory { get; set; } = string.Empty;
    }
}
