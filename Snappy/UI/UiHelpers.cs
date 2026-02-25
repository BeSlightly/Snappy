namespace Snappy.UI;

public static class UiHelpers
{
    public static bool DrawStretchedIconButtonWithText(
        FontAwesomeIcon icon,
        string text,
        string tooltip,
        bool disabled = false,
        float? fixedWidth = null
    )
    {
        var innerSpacing = ImGui.GetStyle().ItemInnerSpacing.X;
        Vector2 iconSize;
        using (var font = ImRaii.PushFont(UiBuilder.IconFont))
        {
            iconSize = Im.Font.CalculateSize(icon.ToIconString());
        }

        var textSize = Im.Font.CalculateSize(text);
        var framePadding = ImGui.GetStyle().FramePadding;

        var contentMaxHeight = Math.Max(iconSize.Y, textSize.Y);
        var buttonHeight =
            contentMaxHeight + framePadding.Y * 2 + 8f * ImGuiHelpers.GlobalScale;
        var buttonSize = new Vector2(fixedWidth ?? -1, buttonHeight);

        var result = false;
        var buttonId = $"##{icon}{text}_stretched";
        using (var d = ImRaii.Disabled(disabled))
        {
            result = Im.Button(buttonId, buttonSize);
        }

        Im.Tooltip.OnHover(HoveredFlags.AllowWhenDisabled, tooltip);

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
                    flags: (InputTextFlags)(ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll)
                ))
                onCommit();
        }

        ImGui.SameLine();
        if (IconButton(FontAwesomeIcon.Check)) onCommit();

        ImGui.SameLine();
        if (IconButton(FontAwesomeIcon.Times)) onCancel();
    }

    public static bool ButtonEx(string label, string tooltip, Vector2 size, bool disabled = false)
    {
        using var _ = ImRaii.Disabled(disabled);
        var ret = Im.Button(label, size);
        Im.Tooltip.OnHover(HoveredFlags.AllowWhenDisabled, tooltip);
        return ret && !disabled;
    }

    public static bool IconButton(FontAwesomeIcon icon, string tooltip = "", Vector2 size = default, bool disabled = false)
    {
        var actualSize = size == default ? new Vector2(ImGui.GetFrameHeight(), ImGui.GetFrameHeight()) : size;
        bool ret;
        using (var disabledScope = ImRaii.Disabled(disabled))
        using (var iconFont = ImRaii.PushFont(UiBuilder.IconFont))
        {
            ret = Im.Button(icon.ToIconString(), actualSize);
        }

        if (!string.IsNullOrEmpty(tooltip))
            Im.Tooltip.OnHover(HoveredFlags.AllowWhenDisabled, tooltip);

        return ret && !disabled;
    }
}
