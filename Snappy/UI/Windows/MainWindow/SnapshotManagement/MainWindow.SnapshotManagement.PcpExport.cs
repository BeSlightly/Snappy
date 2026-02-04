using System.Globalization;
using Dalamud.Interface.Colors;
using ECommons.ExcelServices;

namespace Snappy.UI.Windows;

public partial class MainWindow
{
    private void DrawPcpExportTab()
    {
        // Content container (use default styling similar to Penumbra Effective Changes)
        using var _pcpChild = ImUtf8.Child("PcpExportContent", new Vector2(0, -1), false, ImGuiWindowFlags.None);
        if (!_pcpChild)
            return;
        // Instruction (subtle helper text)
        using (var _pcpTextGrey = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey))
        {
            ImUtf8.Text(
                "Select which Glamourer and Customize+ history entries to include in the PCP export.\nIf no selection is made, the latest entry will be used for each."u8);
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
            ImUtf8.Text("Glamourer Entry"u8);
            ImGui.Spacing();

            var glamourerPreview = _pcpSelectedGlamourerEntry != null
                ? FormatHistoryEntryPreview(_pcpSelectedGlamourerEntry)
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
                    var label = FormatHistoryEntryPreview(entry);
                    var isSelected = ReferenceEquals(_pcpSelectedGlamourerEntry, entry);
                    if (ImGui.Selectable(label, isSelected))
                        _pcpSelectedGlamourerEntry = entry;
                    if (isSelected) ImGui.SetItemDefaultFocus();
                }

                ImGui.EndCombo();
            }

            ImUtf8.HoverTooltip(
                "Pick a specific Glamourer design to include in the PCP. If not selected, the latest design will be used.");

            // Customize+ selector
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImUtf8.Text("Customize+ Entry"u8);
            ImGui.Spacing();

            var customizePreview = _pcpSelectedCustomizeEntry != null
                ? FormatHistoryEntryPreview(_pcpSelectedCustomizeEntry)
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
                    var label = FormatHistoryEntryPreview(entry);
                    var isSelected = ReferenceEquals(_pcpSelectedCustomizeEntry, entry);
                    if (ImGui.Selectable(label, isSelected))
                        _pcpSelectedCustomizeEntry = entry;
                    if (isSelected) ImGui.SetItemDefaultFocus();
                }

                ImGui.EndCombo();
            }

            ImUtf8.HoverTooltip(
                "Pick a specific Customize+ template to include in the PCP. If not selected, the latest template will be used.");

            ImGui.EndTable();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Player details overrides
        if (ImGui.BeginTable("PcpExportPlayerDetails", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Left", ImGuiTableColumnFlags.WidthStretch, 0.5f);
            ImGui.TableSetupColumn("Right", ImGuiTableColumnFlags.WidthStretch, 0.5f);

            // Player Name input
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImUtf8.Text("Player Name"u8);
            ImGui.Spacing();
            ImGui.SetNextItemWidth(-1);
            ImUtf8.InputText(
                "##PcpPlayerName"u8,
                ref _pcpPlayerNameOverride,
                flags: ImGuiInputTextFlags.AutoSelectAll
            );
            ImUtf8.HoverTooltip(
                "Name written to PCP's character.json Actor.PlayerName. Defaults to snapshot's Source Actor."u8);

            // Homeworld selection (reusing searchable combo pattern)
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImUtf8.Text("Homeworld"u8);
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
            ImUtf8.HoverTooltip("Search and select the player's Homeworld. Written to PCP's Actor.HomeWorld (ID)."u8);

            ImGui.EndTable();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Export button centered with icon (matching PMP style)
        var buttonWidth = 220f * ImGuiHelpers.GlobalScale;
        var cursorX = (ImGui.GetContentRegionAvail().X - buttonWidth) * 0.5f;
        var cursorPos = ImGui.GetCursorPosX();
        ImGui.SetCursorPosX(cursorPos + Math.Max(0, cursorX));

        var exportDisabled = _selectedSnapshot == null || string.IsNullOrWhiteSpace(_pcpPlayerNameOverride);
        var exportTooltip =
            "Export the selected entries to a Penumbra Character Package (.pcp). If an entry is not selected, the latest will be used.";
        if (UiHelpers.DrawStretchedIconButtonWithText(FontAwesomeIcon.FileExport, "Export Selected to PCP",
                exportTooltip, exportDisabled, buttonWidth))
        {
            var snapshot = _selectedSnapshot;
            if (snapshot != null)
            {
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
                            _pcpManager.ExportPcp(snapshotPath, path, glam, cust, nameOverride,
                                worldOverride));
                    },
                    _snappy.Configuration.WorkingDirectory
                );
            }
        }
        ImGui.Spacing();
        using (var warningColor = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudYellow))
        {
            var warningText = "Warning: PCP export is experimental. Please report any issues on GitHub.";
            var textWidth = ImUtf8.CalcTextSize(warningText).X;
            var availableWidth = ImGui.GetContentRegionAvail().X;

            // Calculate the starting X position to center the text
            var cursorPosX = (availableWidth - textWidth) * 0.5f;
            if (cursorPosX > 0) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + cursorPosX);

            // Use ImUtf8.Text for precise centering
            ImUtf8.Text(warningText);
        }
    }

    private static string FormatHistoryEntryPreview(HistoryEntryBase entry)
    {
        DateTime.TryParse(entry.Timestamp, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsedUtc);
        var time = parsedUtc == default ? entry.Timestamp : parsedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        var name = string.IsNullOrWhiteSpace(entry.Description) ? "Unnamed Entry" : entry.Description;
        return $"{name}  ({time})";
    }
}
