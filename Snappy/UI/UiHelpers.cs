using Luna;

namespace Snappy.UI;

public static class UiHelpers
{
    public static float GetLabeledIconButtonHeight(float extraPadding = 0f)
    {
        // Original Snappy labeled-button height: content + frame pad + 8px bulk.
        var iconHeight = FontAwesomeIcon.BoxOpen.CalculateSize().Y;
        var textHeight = Im.Font.CalculateSize("Ag").Y;
        var contentHeight = Math.Max(iconHeight, textHeight);
        return contentHeight
               + ImGui.GetStyle().FramePadding.Y * 2
               + 8f * ImGuiHelpers.GlobalScale
               + extraPadding;
    }

    public static bool DrawStretchedIconButtonWithText(
        FontAwesomeIcon icon,
        string text,
        string tooltip,
        bool disabled = false,
        float? fixedWidth = null,
        float? fixedHeight = null
    )
    {
        var buttonWidth = fixedWidth ?? ImGui.GetContentRegionAvail().X;
        var buttonHeight = fixedHeight ?? GetLabeledIconButtonHeight();
        var config = new ImEx.ButtonConfiguration
        {
            Size = new Vector2(buttonWidth, buttonHeight),
            Disabled = disabled,
        };

        return ImEx.Icon.LabeledButton(icon.Icon(), text, tooltip, in config);
    }

    public static void DrawInlineRename(string id, ref string text, Action onCommit, Action onCancel)
    {
        var iconButtonSize = ImGui.GetFrameHeight();
        var buttonsWidth = iconButtonSize * 2 + ImGui.GetStyle().ItemSpacing.X;
        var inputWidth = ImGui.GetContentRegionAvail().X - buttonsWidth - ImGui.GetStyle().ItemSpacing.X;

        ImGui.SetNextItemWidth(inputWidth);
        using (var color = ImRaii.PushColor(ImGuiCol.Border, new Vector4(1, 1, 0, 0.5f)))
        {
            if (Im.Input.Text(
                    "##" + id,
                    ref text,
                    flags: InputTextFlags.EnterReturnsTrue | InputTextFlags.AutoSelectAll
                ))
                onCommit();
        }

        ImGui.SameLine();
        if (IconButton(FontAwesomeIcon.Check, "Confirm")) onCommit();

        ImGui.SameLine();
        if (IconButton(FontAwesomeIcon.Times, "Cancel")) onCancel();
    }

    public static bool ButtonEx(string label, string tooltip, Vector2 size, bool disabled = false)
    {
        using var _ = ImRaii.Disabled(disabled);
        var ret = Im.Button(label, size);
        Im.Tooltip.OnHover(HoveredFlags.AllowWhenDisabled, tooltip);
        return ret && !disabled;
    }

    /// <summary>
    /// Square icon button that stays inside its frame (uses ImEx padding so FA glyphs don't overflow).
    /// </summary>
    public static bool IconButton(FontAwesomeIcon icon, string tooltip = "", Vector2 size = default,
        bool disabled = false)
    {
        var frame = ImGui.GetFrameHeight();
        var actualSize = size == default ? new Vector2(frame, frame) : size;
        var config = new ImEx.ButtonConfiguration
        {
            Size = actualSize,
            Disabled = disabled,
        };

        return ImEx.Icon.Button(icon.Icon(), tooltip, in config);
    }

    public static void DrawStatusIcon(FontAwesomeIcon icon, Vector4 color)
    {
        using var _ = ImRaii.PushColor(ImGuiCol.Text, color);
        // DrawAligned keeps FA icons on the same baseline as adjacent text.
        ImEx.Icon.DrawAligned(icon.Icon());
    }
}
