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
        Im.Input.Text(
            "##SnapshotsFolder",
            ref workingDirectory,
            flags: InputTextFlags.ReadOnly
        );

        ImGui.SameLine();

        if (
            UiHelpers.IconButton(
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
        var revertPadding = ImGui.GetStyle().FramePadding.X * 2 + 16f * ImGuiHelpers.GlobalScale;
        var revertButtonWidth = Im.Font.CalculateSize(revertButtonText).X + revertPadding;
        var revertButtonSize = new Vector2(revertButtonWidth, 0);
        var isRevertDisabled = !activeSnapshotManager.HasActiveSnapshots;

        var buttonPosX = ImGui.GetWindowContentRegionMax().X - revertButtonSize.X;
        ImGui.SetCursorPosX(buttonPosX);

        using var d = ImRaii.Disabled(isRevertDisabled);
        if (Im.Button(revertButtonText, revertButtonSize)) activeSnapshotManager.RevertAllSnapshots();
        Im.Tooltip.OnHover(
            HoveredFlags.AllowWhenDisabled,
            isRevertDisabled
                ? "No snapshots are currently active."
                : "Revert all currently applied snapshots."
        );
    }
}
