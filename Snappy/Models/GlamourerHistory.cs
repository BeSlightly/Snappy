using System;
using System.Collections.Generic;
using System.Globalization;

namespace Snappy.Models;

public class GlamourerHistory
{
    public List<GlamourerHistoryEntry> Entries { get; set; } = new();
}

public record GlamourerHistoryEntry : HistoryEntryBase
{
    public string GlamourerString { get; set; } = string.Empty;
    public string? CustomizeData { get; set; }

    public static GlamourerHistoryEntry Create(string glamourerString, string description, string? fileMapId = null,
        string? customizeData = null)
    {
        return new GlamourerHistoryEntry
        {
            Timestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            Description = description,
            GlamourerString = glamourerString,
            FileMapId = fileMapId,
            CustomizeData = customizeData
        };
    }
}
