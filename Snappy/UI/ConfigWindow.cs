using System;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using ECommons.Configuration;
using ImGuiNET;
using OtterGui.Raii;
using OtterGui.Text;

namespace Snappy.UI
{
    public class ConfigWindow : Window, IDisposable
    {
        private readonly Configuration _configuration;
        private readonly FileDialogManager _fileDialogManager;
        private Plugin Plugin;

        public ConfigWindow(Plugin plugin)
            : base(
                "Snappy Settings",
                ImGuiWindowFlags.NoCollapse
                    | ImGuiWindowFlags.NoScrollbar
                    | ImGuiWindowFlags.NoScrollWithMouse
            )
        {
            this.Size = new Vector2(380, 190) * ImGuiHelpers.GlobalScale;

            this.Plugin = plugin;
            this._configuration = plugin.Configuration;
            this._fileDialogManager = plugin.FileDialogManager;
        }

        public void Dispose() { }

        public override void Draw()
        {
            var enableTheme = false; // Feature is disabled
            using (var d = ImRaii.Disabled(true))
            {
                ImUtf8.Checkbox("Enable Custom Theme"u8, ref enableTheme);
            }
            ImUtf8.HoverTooltip("This feature is currently under development and is disabled.");

            ImGui.Spacing();

            var disableRevert = _configuration.DisableAutomaticRevert;
            if (ImUtf8.Checkbox("Disable Automatic Revert on GPose Exit", ref disableRevert))
            {
                _configuration.DisableAutomaticRevert = disableRevert;
                EzConfig.Save();
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
                    EzConfig.Save();
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
            {
                Plugin.ManuallyRunMigration();
            }
            ImUtf8.HoverTooltip(
                "Manually scans your working directory for old-format snapshots and migrates them to the current format.\n"
                    + "A backup is created before any changes are made."
            );

            ImGui.Separator();

            var versionText =
                $"Snappy v{typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "Unknown"}";
            ImGui.SetCursorPosX(
                ImGui.GetCursorPosX()
                    + ImGui.GetContentRegionAvail().X
                    - ImUtf8.CalcTextSize(versionText).X
            );
            ImUtf8.Text(versionText);
        }
    }
}
