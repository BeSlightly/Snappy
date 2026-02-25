using Dalamud.Interface.Windowing;
using Snappy.Features.Mcdf;
using Snappy.Features.Pcp;
using Snappy.Features.Pmp;
using Snappy.Features.Pmp.ChangedItems;
using Snappy.Services;
using Snappy.Services.SnapshotManager;
using Dalamud.Plugin.Services;

namespace Snappy.UI.Windows;

public partial class MainWindow : Window, IDisposable
{
    private readonly IActiveSnapshotManager _activeSnapshotManager;
    private readonly IActorService _actorService;
    private readonly IIpcManager _ipcManager;
    private readonly IMcdfManager _mcdfManager;
    private readonly IPcpManager _pcpManager;
    private readonly IPmpExportManager _pmpExportManager;
    private readonly ISnapshotChangedItemService _snapshotChangedItemService;
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
    private SnapshotChangedItemSet? _pmpChangedItems;
    private readonly Dictionary<string, bool> _pmpItemSelection = new(StringComparer.OrdinalIgnoreCase);
    private string? _pmpSelectedFileMapId;
    private string? _pmpSelectedHistoryLabel;
    private int? _pmpSelectedHistoryIndex;
    private string? _pmpSelectedGlamourerBase64;
    private bool _pmpIsBuilding;
    private int _pmpBuildToken;
    private bool _pmpNeedsRebuild;
    private string? _pmpBuildError;
    private bool _isRenamingSnapshot;
    private bool _lastIsOpenState;
    private bool _openDeleteSnapshotPopup;
    private bool _openRenameActorPopup;
    private Snapshot? _selectedSnapshot;
    private SnapshotInfo? _selectedSnapshotInfo;

    private Luna.IFileSystemData<Snapshot>[] _snapshotList = Array.Empty<Luna.IFileSystemData<Snapshot>>();
    private string _tempHistoryEntryName = string.Empty;
    private string _tempSnapshotName = string.Empty;
    private string _tempSourceActorName = string.Empty;
    private WorldSelector _pcpWorldSelector = new("##pcpWorld");

    public MainWindow(Snappy snappy, IActorService actorService, IActiveSnapshotManager activeSnapshotManager,
        IMcdfManager mcdfManager, IPcpManager pcpManager,
        IPmpExportManager pmpExportManager, ISnapshotChangedItemService snapshotChangedItemService,
        ISnapshotApplicationService snapshotApplicationService, ISnapshotFileService snapshotFileService,
        ISnapshotIndexService snapshotIndexService, IIpcManager ipcManager)
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
        _snapshotChangedItemService = snapshotChangedItemService;
        _snapshotApplicationService = snapshotApplicationService;
        _snapshotFileService = snapshotFileService;
        _snapshotIndexService = snapshotIndexService;
        _ipcManager = ipcManager;
        _snapshotCombo = new SnapshotCombo(() => _snapshotList);
        _snapshotCombo.SelectionChanged += OnSnapshotSelectionChanged;

        // Configure world selector
        _pcpWorldSelector.EmptyName = "Use snapshot's world";
        _pcpWorldSelector.DisplayCurrent = false;

        // Load snapshots immediately when the window is created
        LoadSnapshots();

        Svc.Framework.Update += OnFrameworkUpdate;

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
        Svc.Framework.Update -= OnFrameworkUpdate;
        _snappy.GPoseService.GPoseExited -= ClearActorSelection;
        _snappy.SnapshotsUpdated -= OnSnapshotsChanged;
    }

    public override void Update()
    {
        if (_lastIsOpenState != IsOpen)
        {
            _lastIsOpenState = IsOpen;
            _ipcManager.SetUiOpen(IsOpen);
            if (IsOpen)
            {
                _actorCacheDirty = true;
                _actorFilterDirty = true;
                _selectedActorStateDirty = true;
                _nextActorCacheRefreshTick = 0;
                _nextSelectedActorRefreshTick = 0;
            }
        }

        base.Update();
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!IsOpen)
            return;

        RefreshSelectableActorsCacheIfNeeded();
        RebuildFilteredActorRowsIfNeeded();
        UpdateSelectedActorStateIfNeeded();
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
                    Im.Text("ACTOR SELECTION");
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
        MainWindowBottomBar.Draw(_snappy, _activeSnapshotManager);
    }

}
