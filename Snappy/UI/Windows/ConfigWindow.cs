using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using ECommons.Configuration;
using ECommons.GameHelpers;
using Snappy.Services.SnapshotManager;

namespace Snappy.UI.Windows;

public sealed class ConfigWindow : Window
{
    private readonly IActiveSnapshotManager _activeSnapshotManager;
    private readonly Configuration _configuration;
    private readonly Snappy _snappy;

    public ConfigWindow(Snappy snappy, Configuration configuration, IActiveSnapshotManager activeSnapshotManager)
        : base(
            "Snappy Settings",
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(380, 155) * ImGuiHelpers.GlobalScale,
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        _snappy = snappy;
        _configuration = configuration;
        _activeSnapshotManager = activeSnapshotManager;
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
                    _activeSnapshotManager.RevertSnapshotForCharacter(Player.Object);
                }

            _configuration.DisableAutomaticRevert = disableRevert;
            _configuration.Save();
        }

        ImUtf8.HoverTooltip(
            "Keeps snapshots applied on your character until you manually revert them or close the game.\nNormally, they revert when you leave GPose."
        );

        ImGui.Indent();
        using (var d = ImRaii.Disabled(!_configuration.DisableAutomaticRevert))
        {
            var allowOutside = _configuration.AllowOutsideGpose;
            if (
                ImUtf8.Checkbox(
                    "Allow loading to your character outside of GPose",
                    ref allowOutside
                )
            )
            {
                _configuration.AllowOutsideGpose = allowOutside;
                _configuration.Save();
            }
        }

        ImGui.Unindent();

        using (var font = ImRaii.PushFont(UiBuilder.IconFont))
        {
            using var iconColor = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudYellow);
            ImUtf8.Text(FontAwesomeIcon.ExclamationTriangle.ToIconString());
        }

        ImGui.SameLine();

        using (
            var textColor = ImRaii.PushColor(
                ImGuiCol.Text,
                ImGui.GetColorU32(ImGuiCol.TextDisabled)
            )
        )
        {
            ImUtf8.Text("Warning: These features are unsupported and may cause issues.");
        }

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
    }
}
