using Dalamud.Interface.Colors;
using ECommons.ExcelServices;

namespace Snappy.UI.Windows;

public partial class MainWindow
{
    private void DrawCharacterExportTab()
    {
        // Same footer layout as PMP Export: content region + separator + full-width action row.
        var footerHeight = UiHelpers.GetLabeledIconButtonHeight()
                           + ImGui.GetStyle().ItemSpacing.Y * 3
                           + 1f * ImGuiHelpers.GlobalScale;
        var bodyHeight = Math.Max(0f, ImGui.GetContentRegionAvail().Y - footerHeight);

        using (var body = Im.Child.Begin("CharacterExportBody", new Vector2(0, bodyHeight), false,
                   WindowFlags.None))
        {
            if (body)
                DrawCharacterExportBody();
        }

        ImGui.Separator();
        ImGui.Spacing();
        DrawCharacterExportButtons();
    }

    private void DrawCharacterExportBody()
    {
        using (var warningColor = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudYellow))
        {
            Im.TextWrapped(
                "Character export is experimental — please report issues on GitHub.");
        }

        ImGui.Spacing();
        using (var grey = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey))
        {
            Im.TextWrapped(
                "Pick Glamourer / Customize+ entries to include. Leave as latest to use the newest for each.");
        }

        ImGui.Separator();
        ImGui.Spacing();

        Im.Text("History"u8);
        ImGui.Spacing();
        using (var selectors = ImRaii.Table("PcpExportSelectors", 2, ImGuiTableFlags.SizingStretchProp))
        {
            if (selectors)
            {
                ImGui.TableSetupColumn("Left", ImGuiTableColumnFlags.WidthStretch, 0.5f);
                ImGui.TableSetupColumn("Right", ImGuiTableColumnFlags.WidthStretch, 0.5f);

                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                Im.Text("Glamourer"u8);
                ImGui.Spacing();
                ImGui.SetNextItemWidth(-1);

                var glamourerPreview = _pcpSelectedGlamourerEntry != null
                    ? HistoryEntryUtil.FormatEntryPreview(_pcpSelectedGlamourerEntry)
                    : "Use latest entry";
                using (var combo = ImRaii.Combo("##PcpGlamourerEntry", glamourerPreview))
                {
                    if (combo)
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
                    }
                }

                Im.Tooltip.OnHover(
                    "Pick a specific Glamourer design to include in the export. If not selected, the latest design will be used.");

                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                Im.Text("Customize+"u8);
                ImGui.Spacing();
                ImGui.SetNextItemWidth(-1);

                var customizePreview = _pcpSelectedCustomizeEntry != null
                    ? HistoryEntryUtil.FormatEntryPreview(_pcpSelectedCustomizeEntry)
                    : "Use latest entry";
                using (var combo = ImRaii.Combo("##PcpCustomizeEntry", customizePreview))
                {
                    if (combo)
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
                    }
                }

                Im.Tooltip.OnHover(
                    "Pick a specific Customize+ profile/template to include in the export. If not selected, the latest entry will be used.");
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        Im.Text("Character identity (PCP)"u8);
        ImGui.Spacing();
        using (var playerDetails = ImRaii.Table("PcpExportPlayerDetails", 2, ImGuiTableFlags.SizingStretchProp))
        {
            if (playerDetails)
            {
                ImGui.TableSetupColumn("Left", ImGuiTableColumnFlags.WidthStretch, 0.5f);
                ImGui.TableSetupColumn("Right", ImGuiTableColumnFlags.WidthStretch, 0.5f);

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

                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                Im.Text("Home World"u8);
                ImGui.Spacing();

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
                Im.Tooltip.OnHover("Search and select the player's home world. Written to PCP's Actor.HomeWorld (ID)."u8);
            }
        }
    }

    private void DrawCharacterExportButtons()
    {
        var exportDisabled = _selectedSnapshot == null;
        // Two full-height stretch buttons side by side — same height helper as PMP export.
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var buttonWidth = Math.Max(0f, (ImGui.GetContentRegionAvail().X - spacing) * 0.5f);

        var pcpTooltip =
            "Export the selected entries to a Penumbra Character Package (.pcp). If an entry is not selected, the latest will be used.";
        if (UiHelpers.DrawStretchedIconButtonWithText(FontAwesomeIcon.FileExport, "Export PCP",
                pcpTooltip, exportDisabled, buttonWidth))
        {
            ExportSelectedCharacterAsPcp();
        }

        ImGui.SameLine(0, spacing);
        var mcdfTooltip =
            "Export the selected entries to a Mare Chara File (.mcdf). If an entry is not selected, the latest will be used.";
        if (UiHelpers.DrawStretchedIconButtonWithText(FontAwesomeIcon.FileExport, "Export MCDF",
                mcdfTooltip, exportDisabled, buttonWidth))
        {
            ExportSelectedCharacterAsMcdf();
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
