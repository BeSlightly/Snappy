using Dalamud.Interface.Colors;
using Penumbra.UI.Classes;
using Snappy.Features.Pmp.ChangedItems;

namespace Snappy.UI.Windows;

public partial class MainWindow
{
    private void DrawPmpSelectionToolbar()
    {
        var selectedCount = _pmpItemSelection.Count(kvp => kvp.Value);
        var totalCount = _pmpItemSelection.Count;

        if (Im.Button("Select All"u8))
            SetAllPmpSelections(true);

        ImGui.SameLine();
        if (Im.Button("Clear"u8))
            SetAllPmpSelections(false);

        ImGui.SameLine();
        var countText = $"{selectedCount} / {totalCount} selected";
        var countWidth = Im.Font.CalculateSize(countText).X;
        var remaining = ImGui.GetContentRegionAvail().X - countWidth;
        if (remaining > 0)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + remaining);

        ImGui.AlignTextToFramePadding();
        using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey))
            Im.Text(countText);
    }

    private void DrawPmpCategoryTabs()
    {
        if (_pmpChangedItems == null)
            return;

        // Room for ~22px category icons without the previous oversized 30px tabs.
        var scale = ImGuiHelpers.GlobalScale;
        var pad = ImGui.GetStyle().FramePadding;
        using var tabPadding = ImRaii.PushStyle(ImGuiStyleVar.FramePadding,
            new Vector2(pad.X + 6f * scale, pad.Y + 5f * scale));
        using var tabBar = Im.TabBar.Begin("PmpItemCategories"u8);
        if (!tabBar)
            return;

        foreach (var category in _pmpChangedItems.Categories)
        {
            if (category.Items.Count == 0)
                continue;

            // Spaces reserve horizontal room for the overlaid category icon.
            var label = _pmpCategoryIcons.HasIcon(category.Category)
                ? $"     ##PmpCategory_{category.Category}"
                : BuildPmpCategoryLabel(category.Category, category.Items.Count);
            using var tab = Im.TabBar.BeginItem(label);
            _pmpCategoryIcons.DrawTabIcon(category.Category, category.Items.Count);
            if (tab)
            {
                DrawPmpCategoryHeading(category.Category, category.Items.Count);
                DrawPmpItemList(category.Category, category.Items);
            }
        }
    }

    private static void DrawPmpCategoryHeading(ChangedItemIconFlag category, int count)
    {
        var name = category == ChangedItemIconFlag.Unknown
            ? "Misc / Unknown"
            : category.ToNameU8().ToString();

        ImGui.AlignTextToFramePadding();
        Im.Text(name);
        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey))
            Im.Text($"{count} {(count == 1 ? "item" : "items")}");
    }

    private void DrawPmpItemList(ChangedItemIconFlag category, IReadOnlyList<SnapshotChangedItem> items)
    {
        var scale = ImGuiHelpers.GlobalScale;
        // Compact list chrome: smaller checkboxes/rows without affecting category tabs above.
        using var framePad = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(3f, 2f) * scale);
        using var cellPad = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, new Vector2(4f, 2f) * scale);
        using var itemSpacing = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing,
            new Vector2(ImGui.GetStyle().ItemSpacing.X, 2f * scale));

        var tableFlags = ImGuiTableFlags.RowBg
                         | ImGuiTableFlags.SizingStretchProp
                         | ImGuiTableFlags.ScrollY;
        var tableSize = new Vector2(0, Math.Max(0, ImGui.GetContentRegionAvail().Y));
        using var table = ImRaii.Table($"PmpItemsTable_{category}", 3, tableFlags, tableSize);
        if (!table)
            return;

        var checkboxCol = ImGui.GetFrameHeight();
        var filesCol = Math.Max(40f * scale, Im.Font.CalculateSize("9999").X + 6f * scale);
        ImGui.TableSetupColumn("##select", ImGuiTableColumnFlags.WidthFixed, checkboxCol);
        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Files", ImGuiTableColumnFlags.WidthFixed, filesCol);
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableHeadersRow();

        var rowHeight = ImGui.GetFrameHeightWithSpacing();
        using var clipper = new Im.ListClipper(items.Count, rowHeight);
        foreach (var i in clipper)
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
            ImGui.AlignTextToFramePadding();
            Im.Text(item.Name);
            if (!string.IsNullOrWhiteSpace(item.AdditionalData))
            {
                ImGui.SameLine(0, ImGui.GetStyle().ItemInnerSpacing.X);
                using var color = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey);
                Im.Text(item.AdditionalData);
            }

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            var countText = item.Count.ToString();
            var textWidth = Im.Font.CalculateSize(countText).X;
            var avail = ImGui.GetContentRegionAvail().X;
            if (avail > textWidth)
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + avail - textWidth);
            Im.Text(countText);
        }
    }

    private static string BuildPmpCategoryLabel(ChangedItemIconFlag category, int count)
    {
        var name = category == ChangedItemIconFlag.Unknown
            ? "Misc / Unknown"
            : category.ToNameU8().ToString();
        return $"{name} ({count})";
    }
}
