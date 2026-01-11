using OtterGui.Filesystem;
using Snappy.Common;

namespace Snappy.UI.Windows;

public partial class MainWindow
{
    private void OnSnapshotsChanged()
    {
        LoadSnapshots();
        if (_selectedSnapshot != null && Directory.Exists(_selectedSnapshot.FullName))
            _snappy.ExecuteBackgroundTask(LoadHistoryForSelectedSnapshotAsync);
        if (_objIdxSelected != null || _selectedActorAddress != null)
            UpdateSelectedActorState();
    }

    private void OnSnapshotSelectionChanged(
        FileSystem<Snapshot>.Leaf? oldSelection,
        FileSystem<Snapshot>.Leaf? newSelection
    )
    {
        var newSnapshot = newSelection?.Value;
        if (_selectedSnapshot == newSnapshot)
            return;

        _selectedSnapshot = newSnapshot;
        _snappy.ExecuteBackgroundTask(LoadHistoryForSelectedSnapshotAsync);
    }

    private void LoadSnapshots()
    {
        var fs = _snappy.SnapshotFS;
        var selectedPath = _selectedSnapshot?.FullName;

        foreach (var child in fs.Root.GetChildren(ISortMode<Snapshot>.Lexicographical).ToList())
            fs.Delete(child);

        var dir = _snappy.Configuration.WorkingDirectory;
        if (Directory.Exists(dir))
        {
            var snapshotDirs = new DirectoryInfo(dir)
                .GetDirectories()
                .Where(d => File.Exists(Path.Combine(d.FullName, Constants.SnapshotFileName)));

            foreach (var d in snapshotDirs)
            {
                var snapshot = new Snapshot(d.FullName);
                fs.CreateLeaf(fs.Root, snapshot.Name, snapshot);
            }
        }

        _snapshotList = fs
            .Root.GetChildren(ISortMode<Snapshot>.Lexicographical)
            .OfType<FileSystem<Snapshot>.Leaf>()
            .OrderBy(s => s.Name)
            .ToArray();

        var newSelection = Array.Find(_snapshotList, s => s.Value.FullName == selectedPath);
        _snapshotCombo.SetSelection(newSelection);
    }

    private void ClearSnapshotSelection()
    {
        _snapshotCombo.SetSelection(null);
    }

    public void ClearActorSelection()
    {
        ClearSelectedActorState();
    }

    private async Task LoadHistoryForSelectedSnapshotAsync()
    {
        _glamourerHistory = new GlamourerHistory();
        _customizeHistory = new CustomizeHistory();
        _selectedSnapshotInfo = null;
        _pcpSelectedGlamourerEntry = null;
        _pcpSelectedCustomizeEntry = null;
        _pcpPlayerNameOverride = string.Empty;
        _pcpSelectedWorldIdOverride = null;
        _pcpWorldSearch = string.Empty;

        if (_selectedSnapshot == null)
            return;

        var paths = SnapshotPaths.From(_selectedSnapshot.FullName);

        try
        {
            _selectedSnapshotInfo = await JsonUtil.DeserializeAsync<SnapshotInfo>(paths.SnapshotFile);
            _glamourerHistory = await JsonUtil.DeserializeAsync<GlamourerHistory>(paths.GlamourerHistoryFile) ??
                                new GlamourerHistory();
            _customizeHistory = await JsonUtil.DeserializeAsync<CustomizeHistory>(paths.CustomizeHistoryFile) ??
                                new CustomizeHistory();

            // Initialize PCP overrides from snapshot info
            if (_selectedSnapshotInfo != null)
            {
                _pcpPlayerNameOverride = _selectedSnapshotInfo.SourceActor ?? string.Empty;
                _pcpSelectedWorldIdOverride = _selectedSnapshotInfo.SourceWorldId;
            }
        }
        catch (Exception e)
        {
            Notify.Error($"Failed to load history for {_selectedSnapshot.Name}\n{e.Message}");
            PluginLog.Error($"Failed to load history for {_selectedSnapshot.Name}: {e}");
        }
    }

    private void SaveHistory()
    {
        if (_selectedSnapshot == null)
            return;

        var paths = SnapshotPaths.From(_selectedSnapshot.FullName);
        JsonUtil.Serialize(_glamourerHistory, paths.GlamourerHistoryFile);
        JsonUtil.Serialize(_customizeHistory, paths.CustomizeHistoryFile);
    }

    private void SaveSourceActorName()
    {
        if (
            _selectedSnapshot == null
            || _selectedSnapshotInfo == null
            || string.IsNullOrWhiteSpace(_tempSourceActorName)
        )
            return;

        _selectedSnapshotInfo.SourceActor = _tempSourceActorName;

        var paths = SnapshotPaths.From(_selectedSnapshot.FullName);
        try
        {
            JsonUtil.Serialize(_selectedSnapshotInfo, paths.SnapshotFile);
            PluginLog.Debug(
                $"Updated SourceActor for snapshot '{_selectedSnapshot.Name}' to '{_tempSourceActorName}'."
            );
            Notify.Success("Source player name updated successfully.");
        }
        catch (Exception e)
        {
            Notify.Error(
                $"Failed to save updated snapshot info for '{_selectedSnapshot.Name}'\n{e.Message}"
            );
            PluginLog.Error(
                $"Failed to save updated snapshot info for '{_selectedSnapshot.Name}': {e}"
            );
        }

        _snappy.InvokeSnapshotsUpdated();
    }
}
