using Dalamud.Interface.Colors;
using Penumbra.UI;
using Snappy.Features.Pmp.ChangedItems;

namespace Snappy.UI.Windows;

public partial class MainWindow
{
    private void DrawPmpSelectionToolbar()
    {
        var selectedCount = _pmpItemSelection.Count(kvp => kvp.Value);
        var totalCount = _pmpItemSelection.Count;

        if (ImUtf8.Button("Select All"u8))
            SetAllPmpSelections(true);

        ImGui.SameLine();
        if (ImUtf8.Button("Clear All"u8))
            SetAllPmpSelections(false);

        ImGui.SameLine();
        ImUtf8.Text($"Selected {selectedCount} / {totalCount} items");
    }

    private void DrawPmpCategoryTabs()
    {
        if (_pmpChangedItems == null)
            return;

        using var tabBar = ImUtf8.TabBar("PmpItemCategories"u8);
        if (!tabBar)
            return;

        foreach (var category in _pmpChangedItems.Categories)
        {
            if (category.Items.Count == 0)
                continue;

            var label = BuildPmpCategoryLabel(category.Category, category.Items.Count);
            using var tab = ImUtf8.TabItem(label);
            if (tab)
                DrawPmpItemList(category.Category, category.Items);
        }
    }

    private void DrawPmpItemList(ChangedItemIconFlag category, IReadOnlyList<SnapshotChangedItem> items)
    {
        var tableFlags = ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY;
        var tableSize = new Vector2(0, Math.Max(0, ImGui.GetContentRegionAvail().Y));
        if (!ImGui.BeginTable($"PmpItemsTable_{category}", 3, tableFlags, tableSize))
            return;

        ImGui.TableSetupColumn(string.Empty, ImGuiTableColumnFlags.WidthFixed, 24f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Files", ImGuiTableColumnFlags.WidthFixed, 60f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableHeadersRow();

        var clipper = new ImGuiListClipper();
        clipper.Begin(items.Count);
        while (clipper.Step())
        {
            for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
            {
                var item = items[i];
                var isSelected = _pmpItemSelection.TryGetValue(item.Key, out var selected) && selected;
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                using (ImRaii.PushId(item.Key))
                {
                    if (ImGui.Checkbox("##PmpItemSelect", ref isSelected))
                        _pmpItemSelection[item.Key] = isSelected;
                }

                ImGui.TableNextColumn();
                ImUtf8.Text(item.Name);
                if (!string.IsNullOrWhiteSpace(item.AdditionalData))
                {
                    ImGui.SameLine();
                    using var color = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey);
                    ImUtf8.Text(item.AdditionalData);
                }

                ImGui.TableNextColumn();
                ImUtf8.Text(item.Count.ToString());
            }
        }

        clipper.End();
        ImGui.EndTable();
    }

    private static string BuildPmpCategoryLabel(ChangedItemIconFlag category, int count)
    {
        var name = category == ChangedItemIconFlag.Unknown ? "Misc / Unknown" : category.ToDescription();
        return $"{name} ({count})";
    }
}
