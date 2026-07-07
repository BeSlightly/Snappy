using Dalamud.Interface.Colors;
using ECommons.ExcelServices;

namespace Snappy.UI.Windows;

public partial class MainWindow
{
    private void DrawCharacterExportTab()
    {
        // Content container (use default styling similar to Penumbra Effective Changes)
        using var _pcpChild = Im.Child.Begin("CharacterExportContent", new Vector2(0, -1), false, WindowFlags.None);
        if (!_pcpChild)
            return;
        // Instruction (subtle helper text)
        using (var _pcpTextGrey = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey))
        {
            Im.Text(
                "Select which Glamourer and Customize+ history entries to include in the character export.\nIf no selection is made, the latest entry will be used for each."u8);
        }

        ImGui.Separator();

        // Two columns for selectors
        if (ImGui.BeginTable("PcpExportSelectors", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Left", ImGuiTableColumnFlags.WidthStretch, 0.5f);
            ImGui.TableSetupColumn("Right", ImGuiTableColumnFlags.WidthStretch, 0.5f);

            // Glamourer selector
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            Im.Text("Glamourer Entry"u8);
            ImGui.Spacing();

            var glamourerPreview = _pcpSelectedGlamourerEntry != null
                ? HistoryEntryUtil.FormatEntryPreview(_pcpSelectedGlamourerEntry)
                : "Use latest entry";
            ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo("##PcpGlamourerEntry", glamourerPreview))
            {
                var useLatestSelected = _pcpSelectedGlamourerEntry == null;
                if (ImGui.Selectable("Use latest entry", useLatestSelected))
                    _pcpSelectedGlamourerEntry = null;
                if (useLatestSelected) ImGui.SetItemDefaultFocus();

                for (var i = _glamourerHistory.Entries.Count - 1; i >= 0; i--)
                {
                    var entry = _glamourerHistory.Entries[i];
                    var label = HistoryEntryUtil.FormatEntryPreview(entry);
                    var isSelected = ReferenceEquals(_pcpSelectedGlamourerEntry, entry);
                    if (ImGui.Selectable(label, isSelected))
                        _pcpSelectedGlamourerEntry = entry;
                    if (isSelected) ImGui.SetItemDefaultFocus();
                }

                ImGui.EndCombo();
            }

            Im.Tooltip.OnHover(
                "Pick a specific Glamourer design to include in the export. If not selected, the latest design will be used.");

            // Customize+ selector
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            Im.Text("Customize+ Entry"u8);
            ImGui.Spacing();

            var customizePreview = _pcpSelectedCustomizeEntry != null
                ? HistoryEntryUtil.FormatEntryPreview(_pcpSelectedCustomizeEntry)
                : "Use latest entry";
            ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo("##PcpCustomizeEntry", customizePreview))
            {
                var useLatestSelected = _pcpSelectedCustomizeEntry == null;
                if (ImGui.Selectable("Use latest entry", useLatestSelected))
                    _pcpSelectedCustomizeEntry = null;
                if (useLatestSelected) ImGui.SetItemDefaultFocus();

                for (var i = _customizeHistory.Entries.Count - 1; i >= 0; i--)
                {
                    var entry = _customizeHistory.Entries[i];
                    var label = HistoryEntryUtil.FormatEntryPreview(entry);
                    var isSelected = ReferenceEquals(_pcpSelectedCustomizeEntry, entry);
                    if (ImGui.Selectable(label, isSelected))
                        _pcpSelectedCustomizeEntry = entry;
                    if (isSelected) ImGui.SetItemDefaultFocus();
                }

                ImGui.EndCombo();
            }

            Im.Tooltip.OnHover(
                "Pick a specific Customize+ profile/template to include in the export. If not selected, the latest entry will be used.");

            ImGui.EndTable();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // PCP player details overrides
        if (ImGui.BeginTable("PcpExportPlayerDetails", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Left", ImGuiTableColumnFlags.WidthStretch, 0.5f);
            ImGui.TableSetupColumn("Right", ImGuiTableColumnFlags.WidthStretch, 0.5f);

            // Player Name input
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            Im.Text("Player Name"u8);
            ImGui.Spacing();
            ImGui.SetNextItemWidth(-1);
            Im.Input.Text(
                "##PcpPlayerName"u8,
                ref _pcpPlayerNameOverride,
                flags: InputTextFlags.AutoSelectAll
            );
            Im.Tooltip.OnHover(
                "Name written to PCP's character.json Actor.PlayerName. Defaults to snapshot's Source Actor."u8);

            // Homeworld selection (reusing searchable combo pattern)
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            Im.Text("Homeworld"u8);
            ImGui.Spacing();

            // Use Lifestream-style WorldSelector grouped by Region/Data Center
            var tmpWorldId = _pcpSelectedWorldIdOverride ?? 0; // 0 means 'use snapshot'
            var snapWorldName = _selectedSnapshotInfo?.SourceWorldId is { } swid2
                ? ExcelWorldHelper.GetName((uint)swid2)
                : null;
            _pcpWorldSelector.EmptyName = !string.IsNullOrWhiteSpace(snapWorldName)
                ? $"Use snapshot's world ({snapWorldName})"
                : "Use snapshot's world";
            ImGui.SetNextItemWidth(-1);
            _pcpWorldSelector.Draw(ref tmpWorldId);
            _pcpSelectedWorldIdOverride = tmpWorldId == 0 ? null : tmpWorldId;
            Im.Tooltip.OnHover("Search and select the player's Homeworld. Written to PCP's Actor.HomeWorld (ID)."u8);

            ImGui.EndTable();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var exportDisabled = _selectedSnapshot == null;
        if (ImGui.BeginTable("CharacterExportButtons", 2, ImGuiTableFlags.SizingStretchSame))
        {
            ImGui.TableNextColumn();
            var pcpTooltip =
                "Export the selected entries to a Penumbra Character Package (.pcp). If an entry is not selected, the latest will be used.";
            if (UiHelpers.DrawStretchedIconButtonWithText(FontAwesomeIcon.FileExport, "Export PCP",
                    pcpTooltip, exportDisabled))
            {
                ExportSelectedCharacterAsPcp();
            }

            ImGui.TableNextColumn();
            var mcdfTooltip =
                "Export the selected entries to a Mare Chara File (.mcdf). If an entry is not selected, the latest will be used.";
            if (UiHelpers.DrawStretchedIconButtonWithText(FontAwesomeIcon.FileExport, "Export MCDF",
                    mcdfTooltip, exportDisabled))
            {
                ExportSelectedCharacterAsMcdf();
            }

            ImGui.EndTable();
        }

        ImGui.Spacing();
        using (var warningColor = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudYellow))
        {
            var warningText = "Warning: Character export is experimental. Please report any issues on GitHub.";
            var textWidth = Im.Font.CalculateSize(warningText).X;
            var availableWidth = ImGui.GetContentRegionAvail().X;

            // Calculate the starting X position to center the text
            var cursorPosX = (availableWidth - textWidth) * 0.5f;
            if (cursorPosX > 0) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + cursorPosX);

            // Keep centered text drawing consistent with the warning style.
            Im.Text(warningText);
        }
    }

    private void ExportSelectedCharacterAsPcp()
    {
        var snapshot = _selectedSnapshot;
        if (snapshot == null)
            return;

        var snapshotName = snapshot.Name;
        var snapshotPath = snapshot.FullName;
        _snappy.FileDialogManager.SaveFileDialog(
            "Export PCP",
            ".pcp",
            $"{snapshotName}.pcp",
            ".pcp",
            (status, path) =>
            {
                if (!status || string.IsNullOrEmpty(path))
                    return;

                Notify.Info($"Starting PCP export for '{snapshotName}'...");
                var glam = _pcpSelectedGlamourerEntry;
                var cust = _pcpSelectedCustomizeEntry;
                var nameOverride = _pcpPlayerNameOverride;
                var worldOverride = _pcpSelectedWorldIdOverride;
                _snappy.ExecuteBackgroundTask(() =>
                    _pcpManager.ExportPcp(snapshotPath, path, glam, cust, nameOverride, worldOverride));
            },
            _snappy.Configuration.WorkingDirectory
        );
    }

    private void ExportSelectedCharacterAsMcdf()
    {
        var snapshot = _selectedSnapshot;
        if (snapshot == null)
            return;

        var snapshotName = snapshot.Name;
        var snapshotPath = snapshot.FullName;
        _snappy.FileDialogManager.SaveFileDialog(
            "Export MCDF",
            ".mcdf",
            $"{snapshotName}.mcdf",
            ".mcdf",
            (status, path) =>
            {
                if (!status || string.IsNullOrEmpty(path))
                    return;

                Notify.Info($"Starting MCDF export for '{snapshotName}'...");
                var glam = _pcpSelectedGlamourerEntry;
                var cust = _pcpSelectedCustomizeEntry;
                _snappy.ExecuteBackgroundTask(() => _mcdfManager.ExportMcdf(snapshotPath, path, glam, cust));
            },
            _snappy.Configuration.WorkingDirectory
        );
    }

}
