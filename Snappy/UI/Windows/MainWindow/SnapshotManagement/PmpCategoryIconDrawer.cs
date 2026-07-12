using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Utility;
using Lumina.Data.Files;
using Penumbra.UI.Classes;

namespace Snappy.UI.Windows;

internal sealed class PmpCategoryIconDrawer : IDisposable
{
    private readonly Dictionary<ChangedItemIconFlag, IDalamudTextureWrap> _icons = new();
    private bool _loadAttempted;

    public bool HasIcon(ChangedItemIconFlag category)
    {
        EnsureLoaded();
        return _icons.ContainsKey(category);
    }

    public void DrawTabIcon(ChangedItemIconFlag category, int count = 0)
    {
        if (!_icons.TryGetValue(category, out var icon))
            return;

        var minimum = ImGui.GetItemRectMin();
        var maximum = ImGui.GetItemRectMax();
        var available = maximum - minimum;
        // Fit the tab with a small inset; cap around mid-size (readable, not the old 30px bulk).
        var scale = ImGuiHelpers.GlobalScale;
        var inset = 6f * scale;
        var size = Math.Min(available.Y - inset, 22f * scale);
        if (size <= 0)
            return;

        var topLeft = minimum + (available - new Vector2(size)) / 2f;
        ImGui.GetWindowDrawList().AddImage(icon.Handle, topLeft, topLeft + new Vector2(size));

        if (ImGui.IsItemHovered())
        {
            var name = category == ChangedItemIconFlag.Unknown
                ? "Misc / Unknown"
                : category.ToNameU8().ToString();
            ImGui.SetTooltip(count > 0
                ? $"{name}\n{count} changed {(count == 1 ? "item" : "items")}"
                : name);
        }
    }

    public void Dispose()
    {
        foreach (var icon in _icons.Values.Distinct())
            icon.Dispose();
        _icons.Clear();
    }

    private void EnsureLoaded()
    {
        if (_loadAttempted)
            return;

        _loadAttempted = true;
        try
        {
            using var armouryIcons = Svc.PluginInterface.UiBuilder.LoadUld("ui/uld/ArmouryBoard.uld");
            if (!armouryIcons.Valid)
                return;

            Add(ChangedItemIconFlag.Mainhand,
                armouryIcons.LoadTexturePart("ui/uld/ArmouryBoard_hr1.tex", 0));
            Add(ChangedItemIconFlag.Head,
                armouryIcons.LoadTexturePart("ui/uld/ArmouryBoard_hr1.tex", 1));
            Add(ChangedItemIconFlag.Body,
                armouryIcons.LoadTexturePart("ui/uld/ArmouryBoard_hr1.tex", 2));
            Add(ChangedItemIconFlag.Hands,
                armouryIcons.LoadTexturePart("ui/uld/ArmouryBoard_hr1.tex", 3));
            Add(ChangedItemIconFlag.Legs,
                armouryIcons.LoadTexturePart("ui/uld/ArmouryBoard_hr1.tex", 5));
            Add(ChangedItemIconFlag.Feet,
                armouryIcons.LoadTexturePart("ui/uld/ArmouryBoard_hr1.tex", 6));
            Add(ChangedItemIconFlag.Offhand,
                armouryIcons.LoadTexturePart("ui/uld/ArmouryBoard_hr1.tex", 7));
            Add(ChangedItemIconFlag.Ears,
                armouryIcons.LoadTexturePart("ui/uld/ArmouryBoard_hr1.tex", 8));
            Add(ChangedItemIconFlag.Neck,
                armouryIcons.LoadTexturePart("ui/uld/ArmouryBoard_hr1.tex", 9));
            Add(ChangedItemIconFlag.Wrists,
                armouryIcons.LoadTexturePart("ui/uld/ArmouryBoard_hr1.tex", 10));
            Add(ChangedItemIconFlag.Finger,
                armouryIcons.LoadTexturePart("ui/uld/ArmouryBoard_hr1.tex", 11));
            AddGameTexture(ChangedItemIconFlag.Monster, "ui/icon/062000/062044_hr1.tex");
            AddGameTexture(ChangedItemIconFlag.Demihuman, "ui/icon/062000/062043_hr1.tex");
            AddGameTexture(ChangedItemIconFlag.Customization, "ui/icon/062000/062045_hr1.tex");
            AddGameTexture(ChangedItemIconFlag.Action, "ui/icon/062000/062001_hr1.tex");
            Add(ChangedItemIconFlag.Emote, LoadEmoteTexture());
            Add(ChangedItemIconFlag.Unknown, LoadUnknownTexture());
        }
        catch (Exception ex)
        {
            PluginLog.Warning($"[PMP] Failed to load changed-item category icons: {ex.Message}");
        }
    }

    private void AddGameTexture(ChangedItemIconFlag category, string gamePath)
    {
        var texture = Svc.Data.GetFile<TexFile>(gamePath);
        if (texture != null)
            Add(category, Svc.Texture.CreateFromTexFile(texture));
    }

    private void Add(ChangedItemIconFlag category, IDalamudTextureWrap? texture)
    {
        if (texture != null)
            _icons[category] = texture;
    }

    private static IDalamudTextureWrap? LoadUnknownTexture()
    {
        var texture = Svc.Data.GetFile<TexFile>("ui/uld/levelup2_hr1.tex");
        if (texture == null)
            return null;

        var source = texture.GetRgbaImageData();
        var bytes = new byte[texture.Header.Height * texture.Header.Height * 4];
        var horizontalOffset = 2 * (texture.Header.Height - texture.Header.Width);
        for (var y = 0; y < texture.Header.Height; ++y)
            source.AsSpan(4 * y * texture.Header.Width, 4 * texture.Header.Width)
                .CopyTo(bytes.AsSpan(4 * y * texture.Header.Height + horizontalOffset));

        return Svc.Texture.CreateFromRaw(
            RawImageSpecification.Rgba32(texture.Header.Height, texture.Header.Height),
            bytes,
            "Snappy.PmpUnknownCategory");
    }

    private static unsafe IDalamudTextureWrap? LoadEmoteTexture()
    {
        var texture = Svc.Data.GetFile<TexFile>("ui/icon/000000/000019_hr1.tex");
        if (texture == null)
            return null;

        var bytes = texture.GetRgbaImageData();
        fixed (byte* pointer = bytes)
        {
            var colors = (uint*)pointer;
            for (var i = 0; i < bytes.Length / 4; ++i)
                if (colors[i] == 0xFF000000)
                    bytes[i * 4 + 3] = 0;
        }

        return Svc.Texture.CreateFromRaw(
            RawImageSpecification.Rgba32(texture.Header.Width, texture.Header.Height),
            bytes,
            "Snappy.PmpEmoteCategory");
    }

}
