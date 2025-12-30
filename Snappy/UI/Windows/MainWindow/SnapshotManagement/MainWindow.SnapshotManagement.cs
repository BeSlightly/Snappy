namespace Snappy.UI.Windows;

public partial class MainWindow
{
    private void DrawSnapshotManagementPanel()
    {
        ImUtf8.Text("SNAPSHOT MANAGEMENT"u8);
        ImGui.Separator();

        DrawSnapshotHeader();
        DrawActionButtons();
        ImGui.Spacing();

        if (_selectedSnapshot != null)
            DrawHistoryTabs();
        else if (_snapshotList.Length > 0)
            ImUtf8.Text("Select a snapshot to manage."u8);
        else
            ImUtf8.Text(
                "No snapshots found. Select an actor and click 'Save Snapshot' to create one."u8
            );
    }

    private void DrawHistoryTabs()
    {
        using var tabBar = ImUtf8.TabBar("HistoryTabs"u8);
        if (!tabBar)
            return;

        using (var tab = ImUtf8.TabItem("Glamourer"u8))
        {
            if (tab)
                DrawHistoryList("Glamourer", _glamourerHistory.Entries);
        }

        using (var tab = ImUtf8.TabItem("Customize+"u8))
        {
            if (tab)
                DrawHistoryList("Customize+", _customizeHistory.Entries);
        }

        using (var tab = ImUtf8.TabItem("PCP Export"u8))
        {
            if (tab)
                DrawPcpExportTab();
        }
    }
}
