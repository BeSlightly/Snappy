using ECommons.ExcelServices;
using Luna;
using Snappy.Common;

namespace Snappy.UI.Windows;

public partial class MainWindow
{
    private void OnSnapshotsChanged()
    {
        RefreshSnapshotsInBackground();
    }

    private void OnSnapshotSelectionChanged(
        IFileSystemData<Snapshot>? oldSelection,
        IFileSystemData<Snapshot>? newSelection
    )
    {
        var newSnapshot = newSelection?.Value;
        if (_selectedSnapshot == newSnapshot)
            return;

        _selectedSnapshot = newSnapshot;
        BeginLoadHistoryForSelectedSnapshot();
    }

    private void LoadSnapshots()
        => LoadSnapshots(EnumerateSnapshots());

    private void RefreshSnapshotsInBackground()
    {
        var refreshVersion = ++_snapshotRefreshVersion;
        _snappy.ExecuteBackgroundTask(() =>
        {
            var snapshots = EnumerateSnapshots();
            _snappy.QueueAction(() =>
            {
                if (refreshVersion != _snapshotRefreshVersion)
                    return;

                LoadSnapshots(snapshots);
                if (_selectedSnapshot != null && Directory.Exists(_selectedSnapshot.FullName))
                    BeginLoadHistoryForSelectedSnapshot();
                if (_objIdxSelected != null || _selectedActorAddress != null)
                    UpdateSelectedActorStateIfNeeded(true);
            });
            return Task.CompletedTask;
        });
    }

    private IReadOnlyList<Snapshot> EnumerateSnapshots()
    {
        var directory = _snappy.Configuration.WorkingDirectory;
        if (!Directory.Exists(directory))
            return [];

        try
        {
            return Directory.EnumerateDirectories(directory)
                .Where(path => File.Exists(Path.Combine(path, Constants.SnapshotFileName)))
                .Select(path => new Snapshot(path) { CreatedUtc = GetCreationTimeUtc(path) })
                .ToArray();
        }
        catch (Exception ex)
        {
            PluginLog.Warning($"Failed to enumerate snapshots in '{directory}': {ex.Message}");
            return [];
        }
    }

    private static DateTime GetCreationTimeUtc(string snapshotPath)
    {
        try
        {
            return Directory.GetCreationTimeUtc(snapshotPath);
        }
        catch (Exception ex)
        {
            PluginLog.Warning($"Failed to read the creation time of '{snapshotPath}': {ex.Message}");
            return DateTime.MinValue;
        }
    }

    private void LoadSnapshots(IReadOnlyList<Snapshot> snapshots)
    {
        var fs = _snappy.SnapshotFS;
        var selectedPath = _selectedSnapshot?.FullName;

        foreach (var child in fs.Root.GetChildren(ISortMode.Lexicographical).ToList())
            fs.Delete(child);

        foreach (var snapshot in snapshots)
            fs.CreateDataNode(fs.Root, snapshot.Name, snapshot);

        _snapshotList = SortSnapshots(
            fs.Root.GetChildren(ISortMode.Lexicographical).OfType<IFileSystemData<Snapshot>>());

        var newSelection = Array.Find(_snapshotList, s => s.Value.FullName == selectedPath);
        _snapshotCombo.SetSelection(newSelection);
    }

    private IFileSystemData<Snapshot>[] SortSnapshots(IEnumerable<IFileSystemData<Snapshot>> snapshots)
        => _snappy.Configuration.SnapshotSortMode switch
        {
            SnapshotSortMode.NameDescending => snapshots
                .OrderByDescending(s => s.Value.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            SnapshotSortMode.DateNewestFirst => snapshots
                .OrderByDescending(s => s.Value.CreatedUtc)
                .ThenBy(s => s.Value.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            SnapshotSortMode.DateOldestFirst => snapshots
                .OrderBy(s => s.Value.CreatedUtc)
                .ThenBy(s => s.Value.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            _ => snapshots
                .OrderBy(s => s.Value.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };

    private void SetSnapshotSortMode(SnapshotSortMode mode)
    {
        if (_snappy.Configuration.SnapshotSortMode == mode)
            return;

        _snappy.Configuration.SnapshotSortMode = mode;
        _snappy.Configuration.Save();
        _snapshotList = SortSnapshots(_snapshotList);
    }

    private void ClearSnapshotSelection()
    {
        _snapshotCombo.SetSelection(null);
    }

    public void ClearActorSelection()
    {
        ClearSelectedActorState();
    }

    private void BeginLoadHistoryForSelectedSnapshot()
    {
        var snapshotPath = _selectedSnapshot?.FullName;
        var loadVersion = ++_historyLoadVersion;
        ResetSelectedSnapshotHistory();

        if (string.IsNullOrEmpty(snapshotPath))
            return;

        _snappy.ExecuteBackgroundTask(() => LoadHistoryForSelectedSnapshotAsync(snapshotPath, loadVersion));
    }

    private void ResetSelectedSnapshotHistory()
    {
        _glamourerHistory = new GlamourerHistory();
        _customizeHistory = new CustomizeHistory();
        _selectedSnapshotInfo = null;
        _pcpGlamourerCombo.SetSelection(null);
        _pcpCustomizeCombo.SetSelection(null);
        _pcpPlayerNameOverride = string.Empty;
        _pcpSelectedWorldIdOverride = null;
        _pmpChangedItems = null;
        _pmpItemSelection.Clear();
        _pmpHistoryCombo.SetSelection(null);
        _pmpIsBuilding = false;
        _pmpBuildError = null;
        _pmpNeedsRebuild = true;
        _pmpBuildToken++;
    }

    private async Task LoadHistoryForSelectedSnapshotAsync(string snapshotPath, int loadVersion)
    {
        var paths = SnapshotPaths.From(snapshotPath);

        try
        {
            var snapshotInfo = await JsonUtil.DeserializeStateAsync<SnapshotInfo>(paths.SnapshotFile)
                               ?? throw new InvalidDataException($"Snapshot state is missing from '{snapshotPath}'.");
            var glamourerHistory = await JsonUtil.DeserializeStateAsync<GlamourerHistory>(paths.GlamourerHistoryFile) ??
                                   new GlamourerHistory();
            var customizeHistory = await JsonUtil.DeserializeStateAsync<CustomizeHistory>(paths.CustomizeHistoryFile) ??
                                   new CustomizeHistory();

            _snappy.QueueAction(() =>
            {
                if (!IsCurrentHistoryLoad(snapshotPath, loadVersion))
                    return;

                _selectedSnapshotInfo = snapshotInfo;
                _glamourerHistory = glamourerHistory;
                _customizeHistory = customizeHistory;
                _pcpPlayerNameOverride = snapshotInfo.SourceActor ?? string.Empty;
                _pcpSelectedWorldIdOverride = snapshotInfo.SourceWorldId;
            });
        }
        catch (Exception e)
        {
            _snappy.QueueAction(() =>
            {
                if (!IsCurrentHistoryLoad(snapshotPath, loadVersion))
                    return;

                var snapshotName = _selectedSnapshot?.Name ?? Path.GetFileName(snapshotPath);
                Notify.Error($"Failed to load history for {snapshotName}\n{e.Message}");
                PluginLog.Error($"Failed to load history for {snapshotName}: {e}");
            });
        }
    }

    private bool IsCurrentHistoryLoad(string snapshotPath, int loadVersion)
        => loadVersion == _historyLoadVersion
           && string.Equals(_selectedSnapshot?.FullName, snapshotPath, StringComparison.OrdinalIgnoreCase);

    private bool SaveHistory()
    {
        if (_selectedSnapshot == null)
            return false;

        var paths = SnapshotPaths.From(_selectedSnapshot.FullName);
        return JsonUtil.SerializeAll(
            (_glamourerHistory, paths.GlamourerHistoryFile),
            (_customizeHistory, paths.CustomizeHistoryFile));
    }

    private void SaveSourceActorName()
    {
        if (
            _selectedSnapshot == null
            || _selectedSnapshotInfo == null
            || string.IsNullOrWhiteSpace(_tempSourceActorName)
        )
            return;

        var paths = SnapshotPaths.From(_selectedSnapshot.FullName);
        var previousSourceActor = _selectedSnapshotInfo.SourceActor;
        var previousWorldId = _selectedSnapshotInfo.SourceWorldId;
        var previousWorldName = _selectedSnapshotInfo.SourceWorldName;

        _selectedSnapshotInfo.SourceActor = _tempSourceActorName.Trim();
        if (_tempSourceWorldId > 0)
        {
            _selectedSnapshotInfo.SourceWorldId = _tempSourceWorldId;
            var worldName = ExcelWorldHelper.GetName((uint)_tempSourceWorldId);
            _selectedSnapshotInfo.SourceWorldName = string.IsNullOrWhiteSpace(worldName) ? null : worldName;
        }
        else
        {
            _selectedSnapshotInfo.SourceWorldId = null;
            _selectedSnapshotInfo.SourceWorldName = null;
        }

        if (!JsonUtil.Serialize(_selectedSnapshotInfo, paths.SnapshotFile))
        {
            _selectedSnapshotInfo.SourceActor = previousSourceActor;
            _selectedSnapshotInfo.SourceWorldId = previousWorldId;
            _selectedSnapshotInfo.SourceWorldName = previousWorldName;
            Notify.Error($"Failed to save updated snapshot info for '{_selectedSnapshot.Name}'.");
            return;
        }

        var worldLabel = _selectedSnapshotInfo.SourceWorldName
                         ?? (_selectedSnapshotInfo.SourceWorldId is > 0
                             ? _selectedSnapshotInfo.SourceWorldId.Value.ToString()
                             : "none");
        PluginLog.Debug(
            $"Updated source for snapshot '{_selectedSnapshot.Name}' to '{_selectedSnapshotInfo.SourceActor}' @ {worldLabel}."
        );
        Notify.Success($"Source actor updated to '{_selectedSnapshotInfo.SourceActor}' @ {worldLabel}.");
        _snappy.InvokeSnapshotsUpdated();
    }
}
