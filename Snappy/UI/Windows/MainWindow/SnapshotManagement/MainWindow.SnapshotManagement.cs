namespace Snappy.UI.Windows;

public partial class MainWindow
{
    private void DrawSnapshotManagementPanel()
    {
        Im.Text("SNAPSHOT MANAGEMENT"u8);
        ImGui.Separator();

        DrawSnapshotHeader();
        DrawActionButtons();
        ImGui.Spacing();

        if (_selectedSnapshot != null)
            DrawHistoryTabs();
        else if (_snapshotList.Length > 0)
            Im.Text("Select a snapshot to manage."u8);
        else
            Im.Text(
                "No snapshots found. Select an actor and click 'Save Snapshot' to create one."u8
            );
    }

    private void DrawHistoryTabs()
    {
        using var tabBar = Im.TabBar.Begin("HistoryTabs"u8);
        if (!tabBar)
            return;

        using (var tab = Im.TabBar.BeginItem("Glamourer"u8))
        {
            if (tab)
                DrawHistoryList("Glamourer", _glamourerHistory.Entries);
        }

        using (var tab = Im.TabBar.BeginItem("Customize+"u8))
        {
            if (tab)
                DrawHistoryList("Customize+", _customizeHistory.Entries);
        }

        using (var tab = Im.TabBar.BeginItem("PMP Export"u8))
        {
            if (tab)
                DrawPmpExportTab();
        }

        using (var tab = Im.TabBar.BeginItem("PCP Export"u8))
        {
            if (tab)
                DrawPcpExportTab();
        }
    }
}
