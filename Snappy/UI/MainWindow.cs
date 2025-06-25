using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using ECommons.GameHelpers;
using ECommons.ImGuiMethods;
using ECommons.Logging;
using ImGuiNET;
using Newtonsoft.Json;
using OtterGui.Classes;
using OtterGui.Filesystem;
using OtterGui.Log;
using OtterGui.Raii;
using OtterGui.Text;
using OtterGui.Widgets;
using Snappy.Models;

namespace Snappy.UI;

public partial class MainWindow : Window, IDisposable
{
    private readonly Plugin Plugin;

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
            {
                for (var i = 0; i < Items.Count; ++i)
                {
                    if (ReferenceEquals(Items[i], CurrentSelection))
                    {
                        currentSelected = i;
                        break;
                    }
                }
            }
            return base.UpdateCurrentSelected(currentSelected);
        }

        public void SetSelection(FileSystem<Snapshot>.Leaf? leaf)
        {
            if (ReferenceEquals(CurrentSelection, leaf))
                return;

            var idx = -1;
            if (leaf != null && IsInitialized)
            {
                for (var i = 0; i < Items.Count; ++i)
                {
                    if (ReferenceEquals(Items[i], leaf))
                    {
                        idx = i;
                        break;
                    }
                }
            }
            CurrentSelectionIdx = idx;
            UpdateSelection(leaf);
        }

        protected override string ToString(FileSystem<Snapshot>.Leaf obj) => obj.Name;

        protected override float GetFilterWidth() => _popupWidth;

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

    private FileSystem<Snapshot>.Leaf[] _snapshotList = Array.Empty<FileSystem<Snapshot>.Leaf>();
    private Snapshot? _selectedSnapshot;
    private readonly SnapshotCombo _snapshotCombo;

    private GlamourerHistory _glamourerHistory = new();
    private CustomizeHistory _customizeHistory = new();
    private SnapshotInfo? _selectedSnapshotInfo;

    private object? _historyEntryToRename = null;
    private string _tempHistoryEntryName = string.Empty;
    private object? _historyEntryToDelete = null;

    private bool _isRenamingSnapshot = false;
    private string _tempSnapshotName = string.Empty;
    private bool _openRenameActorPopup = false;
    private string _tempSourceActorName = string.Empty;
    private bool _openDeleteSnapshotPopup = false;
    private bool _popupDummy = true;

    public MainWindow(Plugin plugin)
        : base(
            $"Snappy v{plugin.Version}",
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse
        )
    {
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(800, 500),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };

        this.Plugin = plugin;
        _snapshotCombo = new SnapshotCombo(() => _snapshotList, plugin.Log);
        _snapshotCombo.SelectionChanged += OnSnapshotSelectionChanged;

        this.TitleBarButtons.Add(
            new()
            {
                Icon = FontAwesomeIcon.Cog,
                IconOffset = new Vector2(2, 1.5f),
                Click = _ => Plugin.DrawConfigUI(),
                ShowTooltip = () => ImGui.SetTooltip("Snappy Settings"),
            }
        );

        Plugin.SnapshotsUpdated += OnSnapshotsChanged;
        Plugin.SnapshotManager.GPoseExited += ClearActorSelection;
    }

    private void OnSnapshotsChanged()
    {
        LoadSnapshots();
        if (player != null)
        {
            UpdateSelectedActorState();
        }
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
        LoadHistoryForSelectedSnapshot();
    }

    private void LoadSnapshots()
    {
        var fs = Plugin.SnapshotFS;
        var selectedPath = _selectedSnapshot?.FullName;

        foreach (var child in fs.Root.GetChildren(ISortMode<Snapshot>.Lexicographical).ToList())
            fs.Delete(child);

        var dir = Plugin.Configuration.WorkingDirectory;
        if (Directory.Exists(dir))
        {
            var snapshotDirs = new DirectoryInfo(dir)
                .GetDirectories()
                .Where(d => File.Exists(Path.Combine(d.FullName, "snapshot.json")));

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

    public void Dispose()
    {
        Plugin.SnapshotManager.GPoseExited -= ClearActorSelection;
        Plugin.SnapshotsUpdated -= OnSnapshotsChanged;
    }

    public void ClearActorSelection()
    {
        ClearSelectedActorState();
    }

    private void LoadHistoryForSelectedSnapshot()
    {
        _glamourerHistory = new();
        _customizeHistory = new();
        _selectedSnapshotInfo = null;

        if (_selectedSnapshot == null)
            return;

        var glamourerPath = Path.Combine(_selectedSnapshot.FullName, "glamourer_history.json");
        var customizePath = Path.Combine(_selectedSnapshot.FullName, "customize_history.json");
        var snapshotInfoPath = Path.Combine(_selectedSnapshot.FullName, "snapshot.json");

        try
        {
            if (File.Exists(snapshotInfoPath))
                _selectedSnapshotInfo = JsonConvert.DeserializeObject<SnapshotInfo>(
                    File.ReadAllText(snapshotInfoPath)
                );

            if (File.Exists(glamourerPath))
                _glamourerHistory =
                    JsonConvert.DeserializeObject<GlamourerHistory>(File.ReadAllText(glamourerPath))
                    ?? new();

            if (File.Exists(customizePath))
                _customizeHistory =
                    JsonConvert.DeserializeObject<CustomizeHistory>(File.ReadAllText(customizePath))
                    ?? new();
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
        var glamourerPath = Path.Combine(_selectedSnapshot.FullName, "glamourer_history.json");
        var customizePath = Path.Combine(_selectedSnapshot.FullName, "customize_history.json");
        File.WriteAllText(
            glamourerPath,
            JsonConvert.SerializeObject(_glamourerHistory, Formatting.Indented)
        );
        File.WriteAllText(
            customizePath,
            JsonConvert.SerializeObject(_customizeHistory, Formatting.Indented)
        );
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

        var snapshotInfoPath = Path.Combine(_selectedSnapshot.FullName, "snapshot.json");
        try
        {
            File.WriteAllText(
                snapshotInfoPath,
                JsonConvert.SerializeObject(_selectedSnapshotInfo, Formatting.Indented)
            );
            PluginLog.Debug(
                $"Updated SourceActor for snapshot '{_selectedSnapshot.Name}' to '{_tempSourceActorName}'."
            );
            Notify.Success("Source player name updated successfully.");
        }
        catch (Exception e)
        {
            Notify.Error(
                $"Failed to save updated snapshot.json for '{_selectedSnapshot.Name}'\n{e.Message}"
            );
            PluginLog.Error(
                $"Failed to save updated snapshot.json for '{_selectedSnapshot.Name}': {e}"
            );
        }

        Plugin.InvokeSnapshotsUpdated();
    }

    private bool DrawStretchedIconButtonWithText(
        FontAwesomeIcon icon,
        string text,
        string tooltip,
        bool disabled = false
    )
    {
        var innerSpacing = ImGui.GetStyle().ItemInnerSpacing.X;
        Vector2 iconSize;
        using (var font = ImRaii.PushFont(UiBuilder.IconFont))
        {
            iconSize = ImUtf8.CalcTextSize(icon.ToIconString());
        }
        var textSize = ImUtf8.CalcTextSize(text);
        var framePadding = ImGui.GetStyle().FramePadding;

        var contentMaxHeight = Math.Max(iconSize.Y, textSize.Y);
        var buttonHeight =
            contentMaxHeight + (framePadding.Y * 2) + (8f * ImGuiHelpers.GlobalScale);
        var buttonSize = new Vector2(-1, buttonHeight);

        var result = false;
        var buttonId = $"##{icon}{text}_stretched";
        using (var d = ImRaii.Disabled(disabled))
        {
            result = ImUtf8.Button(buttonId, buttonSize);
        }
        ImUtf8.HoverTooltip(ImGuiHoveredFlags.AllowWhenDisabled, tooltip);

        var drawList = ImGui.GetWindowDrawList();
        var buttonRectMin = ImGui.GetItemRectMin();
        var buttonRectMax = ImGui.GetItemRectMax();
        var textColor = ImGui.GetColorU32(disabled ? ImGuiCol.TextDisabled : ImGuiCol.Text);

        var totalContentWidth = iconSize.X + innerSpacing + textSize.X;
        var contentStartX =
            buttonRectMin.X + (buttonRectMax.X - buttonRectMin.X - totalContentWidth) / 2;

        var iconStartY = buttonRectMin.Y + (buttonHeight - iconSize.Y) / 2;
        var textStartY = buttonRectMin.Y + (buttonHeight - textSize.Y) / 2;

        using (var font = ImRaii.PushFont(UiBuilder.IconFont))
        {
            drawList.AddText(
                new Vector2(contentStartX, iconStartY),
                textColor,
                icon.ToIconString()
            );
        }
        drawList.AddText(
            new Vector2(contentStartX + iconSize.X + innerSpacing, textStartY),
            textColor,
            text
        );

        return result && !disabled;
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
            using (
                var color = ImRaii.PushColor(ImGuiCol.ChildBg, ImGui.GetColorU32(ImGuiCol.FrameBg))
            )
            {
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
            }

            ImGui.TableNextColumn();
            using (
                var color = ImRaii.PushColor(ImGuiCol.ChildBg, ImGui.GetColorU32(ImGuiCol.FrameBg))
            )
            {
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
        }

        ImGui.Separator();
        DrawBottomBar();
    }

    private void HandlePopups()
    {
        if (_historyEntryToDelete != null)
        {
            ImUtf8.OpenPopup("Delete History Entry");
        }
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
                ref _popupDummy,
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
                ref _popupDummy,
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
                        Plugin.InvokeSnapshotsUpdated();
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
                if (ImUtf8.Button("Cancel", new Vector2(120, 0)))
                {
                    ImGui.CloseCurrentPopup();
                }
            }
        }

        using (
            var modal = ImUtf8.Modal(
                "Rename Source Actor"u8,
                ref _popupDummy,
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
                if (ImGui.IsWindowAppearing())
                {
                    ImGui.SetKeyboardFocusHere(-1);
                }

                ImGui.Separator();

                bool isInvalidName = string.IsNullOrWhiteSpace(_tempSourceActorName);

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
                if (ImUtf8.Button("Cancel", new Vector2(120, 0)))
                {
                    ImGui.CloseCurrentPopup();
                }
            }
        }
    }

    private void DrawBottomBar()
    {
        var workingDirectory = Plugin.Configuration.WorkingDirectory;

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
                tooltip: "Select Snapshots Folder",
                size: buttonSize,
                disabled: false
            )
        )
        {
            Plugin.FileDialogManager.OpenFolderDialog(
                "Where do you want to save your snaps?",
                (status, path) =>
                {
                    if (!status || string.IsNullOrEmpty(path) || !Directory.Exists(path))
                        return;
                    Plugin.Configuration.WorkingDirectory = path;
                    ECommons.Configuration.EzConfig.Save();
                    Notify.Success("Working directory updated.");
                    Plugin.InvokeSnapshotsUpdated();
                }
            );
        }

        ImGui.SameLine();

        var revertButtonText = "Revert All";
        var revertButtonSize = new Vector2(100 * ImGuiHelpers.GlobalScale, 0);
        var isRevertDisabled = !Plugin.SnapshotManager.HasActiveSnapshots;

        var buttonPosX = ImGui.GetWindowContentRegionMax().X - revertButtonSize.X;
        ImGui.SetCursorPosX(buttonPosX);

        using var d = ImRaii.Disabled(isRevertDisabled);
        if (ImUtf8.Button(revertButtonText, revertButtonSize))
        {
            Plugin.SnapshotManager.RevertAllSnapshots();
        }
        ImUtf8.HoverTooltip(
            ImGuiHoveredFlags.AllowWhenDisabled,
            isRevertDisabled
                ? "No snapshots are currently active."
                : "Revert all currently applied snapshots."
        );
    }
}
