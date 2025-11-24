using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.GameHelpers;
using ECommons.ImGuiMethods;
using Lumina.Excel.Sheets;

namespace Snappy.UI;

// Adapted from Lifestream NightmareUI WorldSelector for grouped region/DC world selection.
public class WorldSelector
{
    private string _worldFilter = string.Empty;
    private bool _worldFilterActive;

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
        ImGui.PushID(_id);
        string name;
        if (worldConfig == 0)
            name = EmptyName ?? "Not selected";
        else
            name = ExcelWorldHelper.GetName((uint)worldConfig);

        if (ImGui.BeginCombo("", name, flags))
        {
            DrawInternal(ref worldConfig);
            ImGui.EndCombo();
        }

        ImGui.PopID();
    }

    private void DrawInternal(ref int worldConfig)
    {
        ImGuiEx.SetNextItemFullWidth();
        if (ImGui.IsWindowAppearing())
            ImGui.SetKeyboardFocusHere();
        ImGui.InputTextWithHint("##worldfilter", "Search...", ref _worldFilter, 50);

        Dictionary<ExcelWorldHelper.Region, Dictionary<uint, List<uint>>> regions = new();
        foreach (var region in Enum.GetValues<ExcelWorldHelper.Region>())
        {
            regions[region] = new Dictionary<uint, List<uint>>();
            foreach (var dc in Svc.Data.GetExcelSheet<WorldDCGroupType>()!)
                if (dc.Region == (byte)region)
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

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 1));

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
            ImGui.SetNextItemOpen(false);
            var current = Player.Object.CurrentWorld.RowId;
            if (ImGuiEx.TreeNode($"Current: {ExcelWorldHelper.GetName(current)}",
                    ImGuiTreeNodeFlags.NoTreePushOnOpen | ImGuiTreeNodeFlags.Bullet | (current == worldConfig
                        ? ImGuiTreeNodeFlags.Selected
                        : ImGuiTreeNodeFlags.None)))
            {
                worldConfig = (int)current;
                ImGui.CloseCurrentPopup();
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
                        ImGui.PushID($"{region.Key}");
                        ImGui.GetStateStorage()
                            .SetInt(
                                ImGui.GetID(
                                    $"{Svc.Data.GetExcelSheet<WorldDCGroupType>()!.GetRowOrDefault(v.Key)?.Name}"), 0);
                        ImGui.PopID();
                    }
                }
            }

            if (DefaultAllOpen && ImGui.IsWindowAppearing())
                ImGui.SetNextItemOpen(true);

            if (ImGuiEx.TreeNode($"{region.Key}"))
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
                    if (ImGuiEx.TreeNode($"{dcName}"))
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

                        ImGui.TreePop();
                    }
                }

                ImGui.TreePop();
            }
        }

        ImGui.PopStyleVar();
        _worldFilterActive = _worldFilter != string.Empty;
    }
}