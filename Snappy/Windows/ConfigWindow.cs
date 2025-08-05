using Dalamud.Interface;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using System;
using System.IO;
using System.Numerics;

namespace Snappy.Windows;

public class ConfigWindow : Window, IDisposable
{
    private Configuration Configuration;
    private FileDialogManager FileDialogManager;
    public string Version { get; set; } = string.Empty;

    public ConfigWindow(Plugin plugin) : base(
        "Snappy Settings",
         ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
        ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.Size = new Vector2(350, 120) * ImGuiHelpers.GlobalScale;
        this.SizeCondition = ImGuiCond.Always;

        this.Configuration = plugin.Configuration;
        this.FileDialogManager = plugin.FileDialogManager;
    }

    public void Dispose() { }

    public override void Draw()
    {
        var workingDirectory = Configuration.WorkingDirectory;
        ImGui.InputText("Export Folder", ref workingDirectory, 255, ImGuiInputTextFlags.ReadOnly);
        ImGui.SameLine();
        ImGui.PushFont(UiBuilder.IconFont);
        string folderIcon = FontAwesomeIcon.Folder.ToIconString();
        if (ImGui.Button(folderIcon))
        {
            FileDialogManager.OpenFolderDialog("Where do you want to save your snaps?", (status, path) =>
            {
                if (!status)
                {
                    return;
                }

                if (Directory.Exists(path))
                {
                    this.Configuration.WorkingDirectory = path;
                    this.Configuration.Save();
                }
            });
        }
        ImGui.PopFont();

        ImGui.Text("Glamourer design fallback string");
        string fallbackString = Configuration.FallBackGlamourerString;
        ImGui.InputText("##input-format", ref fallbackString, 2500);
        if (fallbackString != Configuration.FallBackGlamourerString)
        {
            Configuration.FallBackGlamourerString = fallbackString;
            Configuration.Save();
        }
        // Add version label at the bottom
        ImGui.Spacing();
        ImGui.Separator();

        // Display version text with red heart
        ImGui.Text($"Snappy version {typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "Unknown"}. Made with");
        ImGui.SameLine();
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.82f, 0.18f, 0.18f, 1.0f)); // Red color (similar to 0xFF3030D0)
        ImGui.Text(FontAwesomeIcon.Heart.ToIconString());
        ImGui.PopStyleColor();
        ImGui.PopFont();
    }
}