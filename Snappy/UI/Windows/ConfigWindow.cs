using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using ECommons.GameHelpers;
using Snappy.Services;
using Snappy.Services.SnapshotManager;

namespace Snappy.UI.Windows;

public sealed class ConfigWindow : Window
{
    private readonly IActiveSnapshotManager _activeSnapshotManager;
    private readonly Configuration _configuration;
    private readonly Snappy _snappy;
    private readonly IIpcManager _ipcManager;

    public ConfigWindow(Snappy snappy, Configuration configuration, IActiveSnapshotManager activeSnapshotManager,
        IIpcManager ipcManager)
        : base(
            "Snappy Settings",
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(480, 280) * ImGuiHelpers.GlobalScale,
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        _snappy = snappy;
        _configuration = configuration;
        _activeSnapshotManager = activeSnapshotManager;
        _ipcManager = ipcManager;
    }

    public override void Draw()
    {
        using var tabBar = Im.TabBar.Begin("SnappySettingsTabs"u8);
        if (!tabBar)
            return;

        using (var tab = Im.TabBar.BeginItem("General"u8))
        {
            if (tab)
                DrawGeneralTab();
        }

        using (var tab = Im.TabBar.BeginItem("Capture"u8))
        {
            if (tab)
                DrawCaptureTab();
        }

        using (var tab = Im.TabBar.BeginItem("Integrations"u8))
        {
            if (tab)
                DrawIntegrationsTab();
        }
    }

    private void DrawGeneralTab()
    {
        Im.Text("Behavior"u8);
        ImGui.Spacing();

        var disableRevert = _configuration.DisableAutomaticRevert;
        if (Im.Checkbox("Disable automatic revert on GPose exit", ref disableRevert))
        {
            if (_configuration.DisableAutomaticRevert && !disableRevert)
            {
                if (Player.Available)
                {
                    PluginLog.Debug("DisableAutomaticRevert unticked, reverting local player.");
                    var localPlayer = Player.Object;
                    if (localPlayer != null)
                        _activeSnapshotManager.RevertSnapshotForCharacter(localPlayer);
                }
            }

            _configuration.DisableAutomaticRevert = disableRevert;
            _configuration.Save();
        }

        Im.Tooltip.OnHover(
            "When enabled, snapshots stay applied after leaving GPose instead of reverting automatically.");

        using (Im.Indent())
        {
            using (var d = ImRaii.Disabled(!_configuration.DisableAutomaticRevert))
            {
                var allowOutside = _configuration.AllowOutsideGpose;
                if (Im.Checkbox("Allow loading onto your character outside GPose", ref allowOutside))
                {
                    _configuration.AllowOutsideGpose = allowOutside;
                    _configuration.Save();
                }

                Im.Tooltip.OnHover(
                    "Requires automatic revert to be disabled. Apply snapshots to yourself while not in GPose.");
            }
        }

        ImGui.Spacing();
        DrawInlineWarning("These options are unsupported and may cause issues.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        Im.Text("Maintenance"u8);
        ImGui.Spacing();

        if (Im.Button("Run Snapshot Migration Scan", new Vector2(ImGui.GetContentRegionAvail().X, 0)))
            _snappy.ManuallyRunMigration();

        Im.Tooltip.OnHover(
            "Scan the working directory for old-format snapshots and migrate them.\n"
            + "A backup is created before any changes are made.");
    }

    private void DrawCaptureTab()
    {
        using (var grey = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey))
        {
            Im.TextWrapped(
                "By default Snappy reads appearance data from Mare forks when available. Enable the fallback only if that fails.");
        }

        ImGui.Spacing();

        var useLiveSnapshotData = _configuration.UseLiveSnapshotData;
        if (Im.Checkbox("Use Penumbra / Customize+ / Glamourer (fallback)", ref useLiveSnapshotData))
        {
            _configuration.UseLiveSnapshotData = useLiveSnapshotData;
            _configuration.Save();
        }

        Im.Tooltip.OnHover(
            "Fallback for unsupported forks. Mare reflection is usually more complete when a supported fork is available.");

        using (Im.Indent())
        {
            using (var d = ImRaii.Disabled(!useLiveSnapshotData))
            {
                var useIpcResourcePaths = _configuration.UsePenumbraIpcResourcePaths;
                if (Im.Checkbox("Use Penumbra IPC for resource paths", ref useIpcResourcePaths))
                {
                    _configuration.UsePenumbraIpcResourcePaths = useIpcResourcePaths;
                    _configuration.Save();
                }

                Im.Tooltip.OnHover(
                    "IPC only includes currently loaded/on-screen files (not the full collection). Use if reflection fails.");

                var includeTempActors = _configuration.IncludeVisibleTempCollectionActors;
                if (Im.Checkbox("List actors with temporary Penumbra collections", ref includeTempActors))
                {
                    _configuration.IncludeVisibleTempCollectionActors = includeTempActors;
                    _configuration.Save();
                }

                Im.Tooltip.OnHover(
                    "Adds players that currently have a temporary Penumbra collection to the actor list.");
            }
        }
    }

    private void DrawIntegrationsTab()
    {
        using var table = ImRaii.Table("SnappyPluginStatuses", 2,
            ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.PadOuterX);
        if (!table)
            return;

        ImGui.TableNextColumn();
        DrawStatusGroup("Required plugins", _ipcManager.GetPluginStatus(), isMare: false);

        ImGui.TableNextColumn();
        DrawStatusGroup("Mare forks", _ipcManager.GetMarePluginStatus(), isMare: true);
    }

    private void DrawStatusGroup(string title, IReadOnlyDictionary<string, bool> statuses, bool isMare)
    {
        using (var grey = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey))
            Im.Text(title);

        ImGui.Spacing();

        foreach (var (pluginName, isAvailable) in statuses)
        {
            var displayName = FormatPluginName(pluginName);
            Vector4? textColor = null;
            if (isMare && MareForkColors.TryGetByPluginName(pluginName, out var forkColor))
            {
                textColor = new Vector4(
                    forkColor.X,
                    forkColor.Y,
                    forkColor.Z,
                    isAvailable ? forkColor.W : 0.4f);
            }

            DrawPluginStatusRow(displayName, isAvailable, textColor);
        }
    }

    private static string FormatPluginName(string pluginName)
        => pluginName switch
        {
            "CustomizePlus" => "Customize+",
            "LightlessSync" => "Lightless Sync",
            "Snowcloak" => "Snowcloak",
            "MareSempiterne" => "Player Sync",
            _ => pluginName
        };

    private static void DrawPluginStatusRow(string displayName, bool isAvailable, Vector4? textColor = null)
    {
        var icon = isAvailable ? FontAwesomeIcon.Check : FontAwesomeIcon.Times;
        var iconColor = isAvailable ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed;

        UiHelpers.DrawStatusIcon(icon, iconColor);
        ImGui.SameLine();

        var fallbackColor = ImGui.ColorConvertU32ToFloat4(
            ImGui.GetColorU32(isAvailable ? ImGuiCol.Text : ImGuiCol.TextDisabled));
        using var textColorScope = ImRaii.PushColor(ImGuiCol.Text, textColor ?? fallbackColor);
        ImGui.AlignTextToFramePadding();
        Im.Text(displayName);
    }

    private static void DrawInlineWarning(string text)
    {
        UiHelpers.DrawStatusIcon(FontAwesomeIcon.ExclamationTriangle, ImGuiColors.DalamudYellow);
        ImGui.SameLine();
        using (var textColor = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey))
        {
            ImGui.AlignTextToFramePadding();
            Im.TextWrapped(text);
        }
    }
}
