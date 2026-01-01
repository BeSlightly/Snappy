using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Windowing;
using ECommons.Configuration;
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
            MinimumSize = new Vector2(380, 260) * ImGuiHelpers.GlobalScale,
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        _snappy = snappy;
        _configuration = configuration;
        _activeSnapshotManager = activeSnapshotManager;
        _ipcManager = ipcManager;
    }

    public override void Draw()
    {
        var disableRevert = _configuration.DisableAutomaticRevert;
        if (ImUtf8.Checkbox("Disable Automatic Revert on GPose Exit", ref disableRevert))
        {
            if (_configuration.DisableAutomaticRevert && !disableRevert)
                if (Player.Available)
                {
                    PluginLog.Debug("DisableAutomaticRevert unticked, reverting local player.");
                    var localPlayer = Player.Object;
                    if (localPlayer != null)
                        _activeSnapshotManager.RevertSnapshotForCharacter(localPlayer);
                }

            _configuration.DisableAutomaticRevert = disableRevert;
            _configuration.Save();
        }

        ImGui.Indent();
        using (var d = ImRaii.Disabled(!_configuration.DisableAutomaticRevert))
        {
            var allowOutside = _configuration.AllowOutsideGpose;
            if (ImUtf8.Checkbox("Allow loading to your character outside of GPose", ref allowOutside))
            {
                _configuration.AllowOutsideGpose = allowOutside;
                _configuration.Save();
            }
        }

        ImGui.Unindent();

        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            using var color = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudYellow);
            ImUtf8.Text(FontAwesomeIcon.ExclamationTriangle.ToIconString());
        }

        ImGui.SameLine();
        using (var textColor = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey))
        {
            ImUtf8.Text("Warning: These features are unsupported and may cause issues.");
        }

        ImGui.Separator();

        ImUtf8.Text("Snapshot data source:");
        ImGui.Indent();

        var useLiveSnapshotData = _configuration.UseLiveSnapshotData;
        if (ImUtf8.Checkbox("Use Penumbra/Customize+/Glamourer (fallback)", ref useLiveSnapshotData))
        {
            _configuration.UseLiveSnapshotData = useLiveSnapshotData;
            _configuration.Save();
        }
        ImUtf8.HoverTooltip(
            "Fallback for unsupported forks; Mare reflection is usually more complete for supported forks."
        );

        using (var d = ImRaii.Disabled(!useLiveSnapshotData))
        {
            var useIpcResourcePaths = _configuration.UsePenumbraIpcResourcePaths;
            if (ImUtf8.Checkbox("Use Penumbra IPC (resource paths)", ref useIpcResourcePaths))
            {
                _configuration.UsePenumbraIpcResourcePaths = useIpcResourcePaths;
                _configuration.Save();
            }
            ImUtf8.HoverTooltip("IPC uses only currently loaded/on-screen files (no full collection). Use if reflection fails.");

            var includeTempActors = _configuration.IncludeVisibleTempCollectionActors;
            if (ImUtf8.Checkbox("Include visible actors with temporary collections", ref includeTempActors))
            {
                _configuration.IncludeVisibleTempCollectionActors = includeTempActors;
                _configuration.Save();
            }
            ImUtf8.HoverTooltip("Adds players with temporary Penumbra collections to the actor selection.");
        }

        ImGui.Unindent();
        ImGui.Separator();

        if (
            ImUtf8.Button(
                "Run Snapshot Migration Scan",
                new Vector2(ImGui.GetContentRegionAvail().X, 0)
            )
        )
            _snappy.ManuallyRunMigration();
        ImUtf8.HoverTooltip(
            "Manually scans your working directory for old-format snapshots and migrates them to the current format.\n"
            + "A backup is created before any changes are made."
        );

        ImGui.Separator();
        DrawMarePluginStatus();
    }

    private void DrawMarePluginStatus()
    {
        ImUtf8.Text("Mare Plugin Status:");
        ImGui.Indent();

        var mareStatus = _ipcManager.GetMarePluginStatus();

        foreach (var (pluginName, isAvailable) in mareStatus)
        {
            var displayName = pluginName switch
            {
                "LightlessSync" => "Lightless Sync",
                "Snowcloak" => "Snowcloak",
                "MareSempiterne" => "Player Sync",
                _ => pluginName
            };

            var icon = isAvailable ? FontAwesomeIcon.Check : FontAwesomeIcon.Times;
            var iconColor = isAvailable ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed;
            var hasForkColor = TryGetMareForkColor(pluginName, out var forkColor);

            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                using var color = ImRaii.PushColor(ImGuiCol.Text, iconColor);
                ImUtf8.Text(icon.ToIconString());
            }

            ImGui.SameLine();

            var fallbackColor = ImGui.ColorConvertU32ToFloat4(
                ImGui.GetColorU32(isAvailable ? ImGuiCol.Text : ImGuiCol.TextDisabled));
            var textColor = hasForkColor
                ? new Vector4(forkColor.X, forkColor.Y, forkColor.Z, isAvailable ? forkColor.W : 0.4f)
                : fallbackColor;
            using var textColorScope = ImRaii.PushColor(ImGuiCol.Text, textColor);
            ImUtf8.Text(displayName);
        }

        ImGui.Unindent();
    }

    private static bool TryGetMareForkColor(string pluginName, out Vector4 color)
    {
        switch (pluginName)
        {
            case "Snowcloak":
                color = new Vector4(0.4275f, 0.6863f, 1f, 1f);
                return true;
            case "LightlessSync":
                color = new Vector4(0.6784f, 0.5412f, 0.9608f, 1f);
                return true;
            case "MareSempiterne":
                color = new Vector4(0.4745f, 0.8392f, 0.7569f, 1f);
                return true;
            default:
                color = default;
                return false;
        }
    }
}
