using LuminaWorld = Lumina.Excel.Sheets.World;

namespace Snappy;

public sealed partial class Snappy
{
    // World names dictionary for HomeWorld lookup (similar to WhoList)
    public static Dictionary<uint, string> WorldNames { get; private set; } = new();

    private static void InitializeWorldNames()
    {
        try
        {
            // Use the same approach as WhoList to build WorldNames dictionary
            var worldSheet = Svc.Data.GetExcelSheet<LuminaWorld>();
            if (worldSheet != null)
            {
                // Filter to valid, accessible worlds similar to Penumbra DictWorld:
                // - Non-empty name
                // - Has a DataCenter (RowId != 0)
                // - If IsPublic, include
                // - Otherwise, only include if the first character is an uppercase Latin letter
                WorldNames = worldSheet
                    .Where(world =>
                    {
                        var nameStr = world.Name.ToString();
                        if (string.IsNullOrEmpty(nameStr))
                            return false;
                        if (world.DataCenter.RowId == 0)
                            return false;
                        if (world.IsPublic)
                            return true;
                        var first = nameStr[0];
                        return char.IsUpper(first);
                    })
                    .ToDictionary(world => world.RowId, world => world.Name.ToString());

                PluginLog.Debug($"Initialized WorldNames dictionary with {WorldNames.Count} entries.");
            }
            else
            {
                PluginLog.Warning("Failed to load World sheet for WorldNames initialization.");
            }
        }
        catch (Exception ex)
        {
            PluginLog.Error($"Error initializing WorldNames: {ex.Message}");
            WorldNames = new Dictionary<uint, string>();
        }
    }
}
