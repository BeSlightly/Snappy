using OtterGui.Filesystem;
using OtterGui.Log;
using OtterGui.Widgets;

namespace Snappy.UI.Windows;

internal sealed class SnapshotCombo : FilterComboCache<FileSystem<Snapshot>.Leaf>
{
    private float _popupWidth;

    public SnapshotCombo(Func<IReadOnlyList<FileSystem<Snapshot>.Leaf>> generator, Logger log)
        : base(generator, MouseWheelType.None, log)
    {
        SearchByParts = true;
    }

    protected override int UpdateCurrentSelected(int currentSelected)
    {
        if (currentSelected < 0 && CurrentSelection != null)
            for (var i = 0; i < Items.Count; ++i)
                if (ReferenceEquals(Items[i], CurrentSelection))
                {
                    currentSelected = i;
                    break;
                }

        return base.UpdateCurrentSelected(currentSelected);
    }

    public void SetSelection(FileSystem<Snapshot>.Leaf? leaf)
    {
        if (ReferenceEquals(CurrentSelection, leaf))
            return;

        var idx = -1;
        if (leaf != null && IsInitialized)
            for (var i = 0; i < Items.Count; ++i)
                if (ReferenceEquals(Items[i], leaf))
                {
                    idx = i;
                    break;
                }

        CurrentSelectionIdx = idx;
        UpdateSelection(leaf);
    }

    protected override string ToString(FileSystem<Snapshot>.Leaf obj)
    {
        return obj.Name;
    }

    protected override float GetFilterWidth()
    {
        return _popupWidth;
    }

    public bool Draw(string label, string preview, float width)
    {
        _popupWidth = width;
        return Draw(
            label,
            preview,
            string.Empty,
            ref CurrentSelectionIdx,
            width,
            ImGui.GetFrameHeight()
        );
    }
}
