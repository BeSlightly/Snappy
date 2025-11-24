using System;
using System.Collections.Generic;
using System.Globalization;
using Snappy.Common;
using Snappy.Common.Utilities;

namespace Snappy.Models;

public class CustomizeHistory
{
    public List<CustomizeHistoryEntry> Entries { get; set; } = new();
}

public record CustomizeHistoryEntry : HistoryEntryBase
{
    public string CustomizeData { get; set; } = string.Empty; // Base64 from Mare
    public string CustomizeTemplate { get; set; } = string.Empty; // Importable template

    public static CustomizeHistoryEntry CreateFromBase64(string base64Data, string? profileJson, string description,
        string? fileMapId = null)
    {
        var entry = new CustomizeHistoryEntry
        {
            Timestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            Description = description,
            CustomizeData = base64Data,
            FileMapId = fileMapId
        };

        if (!string.IsNullOrEmpty(profileJson))
            try
            {
                entry.CustomizeTemplate = CustomizePlusUtil.CreateCustomizePlusTemplate(profileJson);
            }
            catch (Exception e)
            {
                PluginLog.Error($"Could not generate C+ template for entry '{description}'.\n{e}");
            }

        return entry;
    }
}
