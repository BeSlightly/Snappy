using ECommons.ExcelServices;
using ECommons.GameHelpers;
using Lumina.Excel.Sheets;

namespace Snappy.UI;

// Adapted from Lifestream NightmareUI WorldSelector for grouped region/DC world selection.
public class WorldSelector
{
    private string _worldFilter = string.Empty;
    private bool _worldFilterActive;
    private const int WorldCacheRefreshIntervalMs = 250;
    private long _nextWorldCacheRefreshTick;
    private string _cachedWorldFilter = string.Empty;
    private Dictionary<ExcelWorldHelper.Region, Dictionary<uint, List<uint>>>? _cachedRegions;

    public string? EmptyName { get; set; } = "Use snapshot's world";
    public bool DisplayCurrent { get; set; } = false;
    public bool DefaultAllOpen { get; set; } = false;

    public Predicate<uint>? ShouldHideWorld { get; set; }
        = null; // Optional extra filtering

    private readonly string _id;

    public WorldSelector(string id = "##world")
    {
        _id = id;
    }

    public void Draw(ref int worldConfig, ImGuiComboFlags flags = ImGuiComboFlags.HeightLarge)
    {
        using var id = ImRaii.PushId(_id);
        string name;
        if (worldConfig == 0)
            name = EmptyName ?? "Not selected";
        else
            name = ExcelWorldHelper.GetName((uint)worldConfig);

        using (var combo = ImRaii.Combo("", name, flags))
        {
            if (!combo)
                return;
            DrawInternal(ref worldConfig);
        }
    }

    private void DrawInternal(ref int worldConfig)
    {
        ImGuiEx.SetNextItemFullWidth();
        if (ImGui.IsWindowAppearing())
            ImGui.SetKeyboardFocusHere();
        ImGui.InputTextWithHint("##worldfilter", "Search...", ref _worldFilter, 50);
        var regions = GetCachedRegions();

        using var itemSpacing = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(0, 1));

        if (EmptyName != null)
        {
            ImGui.SetNextItemOpen(false);
            if (ImGuiEx.TreeNode($"{EmptyName}##empty",
                    ImGuiTreeNodeFlags.NoTreePushOnOpen | ImGuiTreeNodeFlags.Bullet |
                    (0 == worldConfig ? ImGuiTreeNodeFlags.Selected : ImGuiTreeNodeFlags.None)))
            {
                worldConfig = 0;
                ImGui.CloseCurrentPopup();
            }
        }

        if (DisplayCurrent && Player.Available)
        {
            var localPlayer = Player.Object;
            var currentWorld = localPlayer?.CurrentWorld.RowId;
            if (currentWorld is { } current)
            {
                ImGui.SetNextItemOpen(false);
                if (ImGuiEx.TreeNode($"Current: {ExcelWorldHelper.GetName(current)}",
                        ImGuiTreeNodeFlags.NoTreePushOnOpen | ImGuiTreeNodeFlags.Bullet | (current == worldConfig
                            ? ImGuiTreeNodeFlags.Selected
                            : ImGuiTreeNodeFlags.None)))
                {
                    worldConfig = (int)current;
                    ImGui.CloseCurrentPopup();
                }
            }
        }

        foreach (var region in regions)
        {
            if (region.Value.Sum(dc => dc.Value.Count) <= 0)
                continue;

            if (_worldFilter != string.Empty && (!_worldFilterActive || ImGui.IsWindowAppearing()))
            {
                ImGui.SetNextItemOpen(true);
            }
            else if (ImGui.IsWindowAppearing() || (_worldFilter == string.Empty && _worldFilterActive))
            {
                var w = worldConfig;
                if (region.Value.Any(d => d.Value.Contains((uint)w)))
                {
                    ImGui.SetNextItemOpen(true);
                }
                else
                {
                    ImGui.SetNextItemOpen(false);
                    foreach (var v in region.Value)
                    {
                        using var id = ImRaii.PushId($"{region.Key}");
                        ImGui.GetStateStorage()
                            .SetInt(
                                ImGui.GetID(
                                    $"{Svc.Data.GetExcelSheet<WorldDCGroupType>()!.GetRowOrDefault(v.Key)?.Name}"), 0);
                    }
                }
            }

            if (DefaultAllOpen && ImGui.IsWindowAppearing())
                ImGui.SetNextItemOpen(true);

            using var regionNode = Im.Tree.Node($"{region.Key}");
            if (regionNode)
            {
                foreach (var dc in region.Value)
                {
                    if (dc.Value.Count <= 0)
                        continue;

                    if (_worldFilter != string.Empty && (!_worldFilterActive || ImGui.IsWindowAppearing()))
                    {
                        ImGui.SetNextItemOpen(true);
                    }
                    else if (ImGui.IsWindowAppearing() || (_worldFilter == string.Empty && _worldFilterActive))
                    {
                        if (dc.Value.Contains((uint)worldConfig))
                            ImGui.SetNextItemOpen(true);
                        else
                            ImGui.SetNextItemOpen(false);
                    }

                    if (DefaultAllOpen && ImGui.IsWindowAppearing())
                        ImGui.SetNextItemOpen(true);

                    var dcName = Svc.Data.GetExcelSheet<WorldDCGroupType>()!.GetRowOrDefault(dc.Key)?.Name;
                    using var dcNode = Im.Tree.Node($"{dcName}");
                    if (dcNode)
                    {
                        foreach (var world in dc.Value)
                        {
                            ImGui.SetNextItemOpen(false);
                            if (ImGuiEx.TreeNode($"{ExcelWorldHelper.GetName(world)}",
                                    ImGuiTreeNodeFlags.NoTreePushOnOpen | ImGuiTreeNodeFlags.Bullet |
                                    (world == worldConfig ? ImGuiTreeNodeFlags.Selected : ImGuiTreeNodeFlags.None)))
                            {
                                worldConfig = (int)world;
                                ImGui.CloseCurrentPopup();
                            }
                        }

                    }
                }
            }
        }
        _worldFilterActive = _worldFilter != string.Empty;
    }

    private Dictionary<ExcelWorldHelper.Region, Dictionary<uint, List<uint>>> GetCachedRegions()
    {
        var now = Environment.TickCount64;
        if (_cachedRegions != null
            && _cachedWorldFilter == _worldFilter
            && now < _nextWorldCacheRefreshTick)
            return _cachedRegions;

        _cachedWorldFilter = _worldFilter;
        _nextWorldCacheRefreshTick = now + WorldCacheRefreshIntervalMs;
        _cachedRegions = BuildRegionMap();
        return _cachedRegions;
    }

    private Dictionary<ExcelWorldHelper.Region, Dictionary<uint, List<uint>>> BuildRegionMap()
    {
        Dictionary<ExcelWorldHelper.Region, Dictionary<uint, List<uint>>> regions = new();
        foreach (var region in Enum.GetValues<ExcelWorldHelper.Region>())
        {
            regions[region] = new Dictionary<uint, List<uint>>();
            foreach (var dc in Svc.Data.GetExcelSheet<WorldDCGroupType>()!)
                if (dc.Region.RowId == (uint)region)
                {
                    regions[region][dc.RowId] = new List<uint>();
                    foreach (var world in ExcelWorldHelper.GetPublicWorlds(dc.RowId))
                        if (_worldFilter == string.Empty
                            || world.Name.ToString().Contains(_worldFilter, StringComparison.OrdinalIgnoreCase)
                            || world.RowId.ToString().Contains(_worldFilter, StringComparison.OrdinalIgnoreCase))
                            if (ShouldHideWorld == null || !ShouldHideWorld(world.RowId))
                                regions[region][dc.RowId].Add(world.RowId);

                    regions[region][dc.RowId] = regions[region][dc.RowId]
                        .OrderBy(ExcelWorldHelper.GetName)
                        .ToList();
                }
        }

        return regions;
    }
}
