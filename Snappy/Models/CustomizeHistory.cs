using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ECommons.Logging;
using Snappy.Utils;

namespace Snappy.Models
{
    public class CustomizeHistory
    {
        public List<CustomizeHistoryEntry> Entries { get; set; } = new();
    }

    public class CustomizeHistoryEntry
    {
        public string Timestamp { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string CustomizeData { get; set; } = string.Empty; // Base64 from Mare
        public string CustomizeTemplate { get; set; } = string.Empty; // Importable template

        public static CustomizeHistoryEntry CreateFromBase64(
            string base64Data,
            string? profileJson,
            string description
        )
        {
            var entry = new CustomizeHistoryEntry
            {
                Timestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                Description = description,
                CustomizeData = base64Data,
            };

            if (!string.IsNullOrEmpty(profileJson))
            {
                try
                {
                    entry.CustomizeTemplate = CustomizePlusUtil.CreateCustomizePlusTemplate(
                        profileJson
                    );
                }
                catch (Exception e)
                {
                    PluginLog.Error(
                        $"Could not generate C+ template for entry '{description}'.\n{e}"
                    );
                }
            }
            return entry;
        }
    }
}
