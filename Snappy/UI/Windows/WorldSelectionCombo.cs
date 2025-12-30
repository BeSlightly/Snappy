using OtterGui.Log;
using OtterGui.Widgets;

namespace Snappy.UI.Windows;

internal sealed class WorldSelectionCombo : FilterComboCache<KeyValuePair<uint, string>>
{
    private float _popupWidth;

    public WorldSelectionCombo(Func<IReadOnlyList<KeyValuePair<uint, string>>> generator, Logger log)
        : base(() =>
        {
            var items = generator();
            var list = new List<KeyValuePair<uint, string>>(items.Count + 1)
            {
                new(0, "Use snapshot's world")
            };
            list.AddRange(items);
            return list;
        }, MouseWheelType.None, log)
    {
    }

    protected override string ToString(KeyValuePair<uint, string> obj)
    {
        return obj.Value;
    }

    protected override float GetFilterWidth()
    {
        return _popupWidth;
    }

    public bool Draw(string label, string preview, float width, ref int currentIdx)
    {
        _popupWidth = width;
        return Draw(label, preview, string.Empty, ref currentIdx, width, ImGui.GetFrameHeight());
    }

    public void SetSelection(int? worldId)
    {
        if (!IsInitialized)
        {
            CurrentSelectionIdx = -1;
            return;
        }

        var id = (uint)(worldId ?? 0);
        var idx = -1;
        for (var i = 0; i < Items.Count; ++i)
            if (Items[i].Key == id)
            {
                idx = i;
                break;
            }

        CurrentSelectionIdx = idx;
        UpdateSelection(idx >= 0 ? Items[idx] : default);
    }
}
