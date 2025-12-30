using Snappy.Services.SnapshotManager;

namespace Snappy.UI.Windows;

internal static class MainWindowBottomBar
{
    public static void Draw(Snappy snappy, IActiveSnapshotManager activeSnapshotManager)
    {
        var workingDirectory = snappy.Configuration.WorkingDirectory;

        const float selectorWidthPercentage = 0.4f;

        var totalSelectorWidth = ImGui.GetContentRegionAvail().X * selectorWidthPercentage;
        var buttonSize = new Vector2(ImGui.GetFrameHeight());
        var itemSpacing = ImGui.GetStyle().ItemSpacing.X;
        var inputWidth = totalSelectorWidth - buttonSize.X - itemSpacing;

        ImGui.SetNextItemWidth(inputWidth);
        ImUtf8.InputText(
            "##SnapshotsFolder",
            ref workingDirectory,
            flags: ImGuiInputTextFlags.ReadOnly
        );

        ImGui.SameLine();

        if (
            ImUtf8.IconButton(
                FontAwesomeIcon.Folder,
                "Select Snapshots Folder",
                buttonSize,
                false
            )
        )
            snappy.FileDialogManager.OpenFolderDialog(
                "Where do you want to save your snaps?",
                (status, path) =>
                {
                    if (!status || string.IsNullOrEmpty(path) || !Directory.Exists(path))
                        return;
                    snappy.Configuration.WorkingDirectory = path;
                    snappy.Configuration.Save();
                    Notify.Success("Working directory updated.");
                    snappy.InvokeSnapshotsUpdated();
                }
            );

        ImGui.SameLine();

        var revertButtonText = "Revert All";
        var revertButtonSize = new Vector2(100 * ImGuiHelpers.GlobalScale, 0);
        var isRevertDisabled = !activeSnapshotManager.HasActiveSnapshots;

        var buttonPosX = ImGui.GetWindowContentRegionMax().X - revertButtonSize.X;
        ImGui.SetCursorPosX(buttonPosX);

        using var d = ImRaii.Disabled(isRevertDisabled);
        if (ImUtf8.Button(revertButtonText, revertButtonSize)) activeSnapshotManager.RevertAllSnapshots();
        ImUtf8.HoverTooltip(
            ImGuiHoveredFlags.AllowWhenDisabled,
            isRevertDisabled
                ? "No snapshots are currently active."
                : "Revert all currently applied snapshots."
        );
    }
}
