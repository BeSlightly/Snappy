using Dalamud.Interface.Windowing;
using ECommons.Configuration;
using OtterGui.Filesystem;
using OtterGui.Log;
using OtterGui.Widgets;
using Snappy.Common;
using Snappy.Features.Mcdf;
using Snappy.Features.Pcp;
using Snappy.Features.Pmp;
using Snappy.Services;
using Snappy.Services.SnapshotManager;

namespace Snappy.UI.Windows;

public partial class MainWindow : Window, IDisposable
{
    private readonly IActiveSnapshotManager _activeSnapshotManager;
    private readonly IActorService _actorService;
    private readonly IIpcManager _ipcManager;
    private readonly IMcdfManager _mcdfManager;
    private readonly IPcpManager _pcpManager;
    private readonly IPmpExportManager _pmpExportManager;
    private readonly Snappy _snappy;
    private readonly ISnapshotApplicationService _snapshotApplicationService;
    private readonly SnapshotCombo _snapshotCombo;
    private readonly ISnapshotFileService _snapshotFileService;
    private readonly ISnapshotIndexService _snapshotIndexService;

    private CustomizeHistory _customizeHistory = new();

    private GlamourerHistory _glamourerHistory = new();
    private HistoryEntryBase? _historyEntryToDelete;

    private HistoryEntryBase? _historyEntryToRename;

    private GlamourerHistoryEntry? _pcpSelectedGlamourerEntry;
    private CustomizeHistoryEntry? _pcpSelectedCustomizeEntry;
    private string _pcpPlayerNameOverride = string.Empty;
    private int? _pcpSelectedWorldIdOverride;
    private string _pcpWorldSearch = string.Empty;
    private bool _isRenamingSnapshot;
    private bool _lastIsOpenState;
    private bool _openDeleteSnapshotPopup;
    private bool _openRenameActorPopup;
    private Snapshot? _selectedSnapshot;
    private SnapshotInfo? _selectedSnapshotInfo;

    private FileSystem<Snapshot>.Leaf[] _snapshotList = Array.Empty<FileSystem<Snapshot>.Leaf>();
    private string _tempHistoryEntryName = string.Empty;
    private string _tempSnapshotName = string.Empty;
    private string _tempSourceActorName = string.Empty;
    private WorldSelector _pcpWorldSelector = new("##pcpWorld");

    public MainWindow(Snappy snappy, IActorService actorService, IActiveSnapshotManager activeSnapshotManager,
        IMcdfManager mcdfManager, IPcpManager pcpManager,
        IPmpExportManager pmpExportManager, ISnapshotApplicationService snapshotApplicationService,
        ISnapshotFileService snapshotFileService, ISnapshotIndexService snapshotIndexService,
        IIpcManager ipcManager)
        : base(
            $"Snappy v{snappy.Version}",
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse
        )
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(800, 500),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        _snappy = snappy;
        _actorService = actorService;
        _activeSnapshotManager = activeSnapshotManager;
        _mcdfManager = mcdfManager;
        _pcpManager = pcpManager;
        _pmpExportManager = pmpExportManager;
        _snapshotApplicationService = snapshotApplicationService;
        _snapshotFileService = snapshotFileService;
        _snapshotIndexService = snapshotIndexService;
        _ipcManager = ipcManager;
        _snapshotCombo = new SnapshotCombo(() => _snapshotList, snappy.Log);
        _snapshotCombo.SelectionChanged += OnSnapshotSelectionChanged;

        // Configure world selector
        _pcpWorldSelector.EmptyName = "Use snapshot's world";
        _pcpWorldSelector.DisplayCurrent = false;

        // Load snapshots immediately when the window is created
        LoadSnapshots();

        TitleBarButtons.Add(
            new TitleBarButton
            {
                Icon = FontAwesomeIcon.Cog,
                IconOffset = new Vector2(2, 1.5f),
                Click = _ => _snappy.DrawConfigUI(),
                ShowTooltip = () => ImGui.SetTooltip("Snappy Settings")
            }
        );

        _snappy.SnapshotsUpdated += OnSnapshotsChanged;
        _snappy.GPoseService.GPoseExited += ClearActorSelection;
    }

    public void Dispose()
    {
        _snappy.GPoseService.GPoseExited -= ClearActorSelection;
        _snappy.SnapshotsUpdated -= OnSnapshotsChanged;
    }

    private void OnSnapshotsChanged()
    {
        LoadSnapshots();
        if (player != null) UpdateSelectedActorState();
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

    public override void Update()
    {
        if (_lastIsOpenState != IsOpen)
        {
            _lastIsOpenState = IsOpen;
            _ipcManager.SetUiOpen(IsOpen);
        }

        base.Update();
    }

    public override void Draw()
    {
        HandlePopups();

        var bottomBarHeight = ImGui.GetFrameHeight() + ImGui.GetStyle().WindowPadding.Y * 2;
        var mainContentHeight = ImGui.GetContentRegionAvail().Y - bottomBarHeight;
        var mainContentSize = new Vector2(0, mainContentHeight);

        using (var table = ImRaii.Table("MainLayout", 2, ImGuiTableFlags.Resizable))
        {
            if (!table)
                return;

            ImGui.TableSetupColumn(
                "Left",
                ImGuiTableColumnFlags.WidthFixed,
                220 * ImGuiHelpers.GlobalScale
            );
            ImGui.TableSetupColumn("Right", ImGuiTableColumnFlags.WidthStretch);

            ImGui.TableNextColumn();
            using (var child = ImRaii.Child("LeftColumnChild", mainContentSize, false))
            {
                if (child)
                {
                    using var padding = ImRaii.PushStyle(
                        ImGuiStyleVar.WindowPadding,
                        new Vector2(8f, 8f) * ImGuiHelpers.GlobalScale
                    );
                    ImUtf8.Text("ACTOR SELECTION");
                    ImGui.Separator();
                    DrawPlayerSelector();
                }
            }

            ImGui.TableNextColumn();
            using (var child = ImRaii.Child("RightColumnChild", mainContentSize, false))
            {
                if (child)
                {
                    using var padding = ImRaii.PushStyle(
                        ImGuiStyleVar.WindowPadding,
                        new Vector2(8f, 8f) * ImGuiHelpers.GlobalScale
                    );
                    DrawSnapshotManagementPanel();
                }
            }
        }

        ImGui.Separator();
        DrawBottomBar();
    }

    private void HandlePopups()
    {
        if (_historyEntryToDelete != null) ImUtf8.OpenPopup("Delete History Entry");
        if (_openDeleteSnapshotPopup)
        {
            ImUtf8.OpenPopup("Delete Snapshot");
            _openDeleteSnapshotPopup = false;
        }

        if (_openRenameActorPopup)
        {
            if (_selectedSnapshotInfo != null)
            {
                _tempSourceActorName = _selectedSnapshotInfo.SourceActor;
                ImUtf8.OpenPopup("Rename Source Actor"u8);
            }

            _openRenameActorPopup = false;
        }

        using (
            var modal = ImUtf8.Modal(
                "Delete History Entry",
                ImGuiWindowFlags.AlwaysAutoResize
            )
        )
        {
            if (modal)
            {
                ImUtf8.Text(
                    "Are you sure you want to delete this history entry?\nThis action cannot be undone."
                );
                ImGui.Separator();
                if (ImUtf8.Button("Yes, Delete", new Vector2(120, 0)))
                {
                    if (_historyEntryToDelete is GlamourerHistoryEntry gEntry)
                        _glamourerHistory.Entries.Remove(gEntry);
                    else if (_historyEntryToDelete is CustomizeHistoryEntry cEntry)
                        _customizeHistory.Entries.Remove(cEntry);

                    SaveHistory();

                    Notify.Success("History entry deleted.");
                    _historyEntryToDelete = null;
                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();
                if (ImUtf8.Button("Cancel", new Vector2(120, 0)))
                {
                    _historyEntryToDelete = null;
                    ImGui.CloseCurrentPopup();
                }
            }
        }

        using (
            var modal = ImUtf8.Modal(
                "Delete Snapshot",
                ImGuiWindowFlags.AlwaysAutoResize
            )
        )
        {
            if (modal)
            {
                ImUtf8.Text(
                    $"Are you sure you want to permanently delete the snapshot '{_selectedSnapshot?.Name}'?\nThis will delete the entire folder and its contents.\nThis action cannot be undone."
                );
                ImGui.Separator();
                if (ImUtf8.Button("Yes, Delete Snapshot", new Vector2(180, 0)))
                {
                    try
                    {
                        var deletedSnapshotName = _selectedSnapshot!.Name;
                        Directory.Delete(_selectedSnapshot!.FullName, true);
                        ClearSnapshotSelection();
                        _snappy.InvokeSnapshotsUpdated();
                        Notify.Success($"Snapshot '{deletedSnapshotName}' deleted successfully.");
                    }
                    catch (Exception e)
                    {
                        Notify.Error($"Could not delete snapshot directory\n{e.Message}");
                        PluginLog.Error($"Could not delete snapshot directory: {e}");
                    }

                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();
                if (ImUtf8.Button("Cancel", new Vector2(120, 0))) ImGui.CloseCurrentPopup();
            }
        }

        using (
            var modal = ImUtf8.Modal(
                "Rename Source Actor"u8,
                ImGuiWindowFlags.AlwaysAutoResize
            )
        )
        {
            if (modal)
            {
                ImUtf8.Text("Enter the new name for the Source Actor of this snapshot.");
                ImUtf8.Text("This name is used to find the snapshot when using 'Update Snapshot'.");
                ImGui.Separator();

                ImGui.SetNextItemWidth(300 * ImGuiHelpers.GlobalScale);
                var enterPressed = ImUtf8.InputText(
                    "##SourceActorName"u8,
                    ref _tempSourceActorName,
                    flags: ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll
                );
                if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere(-1);

                ImGui.Separator();

                var isInvalidName = string.IsNullOrWhiteSpace(_tempSourceActorName);

                using (var d = ImRaii.Disabled(isInvalidName))
                {
                    if (
                        ImUtf8.Button("Save", new Vector2(120, 0))
                        || (enterPressed && !isInvalidName)
                    )
                    {
                        SaveSourceActorName();
                        ImGui.CloseCurrentPopup();
                    }
                }

                ImGui.SameLine();
                if (ImUtf8.Button("Cancel", new Vector2(120, 0))) ImGui.CloseCurrentPopup();
            }
        }
    }

    private void DrawBottomBar()
    {
        var workingDirectory = _snappy.Configuration.WorkingDirectory;

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
            _snappy.FileDialogManager.OpenFolderDialog(
                "Where do you want to save your snaps?",
                (status, path) =>
                {
                    if (!status || string.IsNullOrEmpty(path) || !Directory.Exists(path))
                        return;
                    _snappy.Configuration.WorkingDirectory = path;
                    _snappy.Configuration.Save();
                    Notify.Success("Working directory updated.");
                    _snappy.InvokeSnapshotsUpdated();
                }
            );

        ImGui.SameLine();

        var revertButtonText = "Revert All";
        var revertButtonSize = new Vector2(100 * ImGuiHelpers.GlobalScale, 0);
        var isRevertDisabled = !_activeSnapshotManager.HasActiveSnapshots;

        var buttonPosX = ImGui.GetWindowContentRegionMax().X - revertButtonSize.X;
        ImGui.SetCursorPosX(buttonPosX);

        using var d = ImRaii.Disabled(isRevertDisabled);
        if (ImUtf8.Button(revertButtonText, revertButtonSize)) _activeSnapshotManager.RevertAllSnapshots();
        ImUtf8.HoverTooltip(
            ImGuiHoveredFlags.AllowWhenDisabled,
            isRevertDisabled
                ? "No snapshots are currently active."
                : "Revert all currently applied snapshots."
        );
    }

    private class SnapshotCombo : FilterComboCache<FileSystem<Snapshot>.Leaf>
    {
        private float _popupWidth;

        public SnapshotCombo(Func<IReadOnlyList<FileSystem<Snapshot>.Leaf>> generator, Logger log)
            : base(generator, MouseWheelType.None, log)
        {
            SearchByParts = true;
        }

        protected override int UpdateCurrentSelected(int currentSelected)
        {
            if (currentSelected < 0 && CurrentSelection != null)
                for (var i = 0; i < Items.Count; ++i)
                    if (ReferenceEquals(Items[i], CurrentSelection))
                    {
                        currentSelected = i;
                        break;
                    }

            return base.UpdateCurrentSelected(currentSelected);
        }

        public void SetSelection(FileSystem<Snapshot>.Leaf? leaf)
        {
            if (ReferenceEquals(CurrentSelection, leaf))
                return;

            var idx = -1;
            if (leaf != null && IsInitialized)
                for (var i = 0; i < Items.Count; ++i)
                    if (ReferenceEquals(Items[i], leaf))
                    {
                        idx = i;
                        break;
                    }

            CurrentSelectionIdx = idx;
            UpdateSelection(leaf);
        }

        protected override string ToString(FileSystem<Snapshot>.Leaf obj)
        {
            return obj.Name;
        }

        protected override float GetFilterWidth()
        {
            return _popupWidth;
        }

        public bool Draw(string label, string preview, float width)
        {
            _popupWidth = width;
            return Draw(
                label,
                preview,
                string.Empty,
                ref CurrentSelectionIdx,
                width,
                ImGui.GetFrameHeight()
            );
        }
    }

    private class WorldSelectionCombo : FilterComboCache<KeyValuePair<uint, string>>
    {
        private float _popupWidth;

        public WorldSelectionCombo(Func<IReadOnlyList<KeyValuePair<uint, string>>> generator, Logger log)
            : base(() =>
            {
                var items = generator();
                var list = new List<KeyValuePair<uint, string>>(items.Count + 1)
                {
                    new(0, "Use snapshot's world")
                };
                list.AddRange(items);
                return list;
            }, MouseWheelType.None, log)
        {
        }

        protected override string ToString(KeyValuePair<uint, string> obj)
        {
            return obj.Value;
        }

        protected override float GetFilterWidth()
        {
            return _popupWidth;
        }

        public bool Draw(string label, string preview, float width, ref int currentIdx)
        {
            _popupWidth = width;
            return Draw(label, preview, string.Empty, ref currentIdx, width, ImGui.GetFrameHeight());
        }

        public void SetSelection(int? worldId)
        {
            if (!IsInitialized)
            {
                CurrentSelectionIdx = -1;
                return;
            }

            var id = (uint)(worldId ?? 0);
            var idx = -1;
            for (var i = 0; i < Items.Count; ++i)
                if (Items[i].Key == id)
                {
                    idx = i;
                    break;
                }

            CurrentSelectionIdx = idx;
            UpdateSelection(idx >= 0 ? Items[idx] : default);
        }
    }
}