using ImSharp;
using Luna;

namespace Snappy.UI.Windows;

internal sealed class SnapshotCombo(Func<IReadOnlyList<IFileSystemData<Snapshot>>> generator)
    : SimpleFilterCombo<IFileSystemData<Snapshot>>(SimpleFilterType.Text)
{
    private readonly Func<IReadOnlyList<IFileSystemData<Snapshot>>> _generator = generator;
    private IFileSystemData<Snapshot>? _selection;

    public event Action<IFileSystemData<Snapshot>?, IFileSystemData<Snapshot>?>? SelectionChanged;

    public void SetSelection(IFileSystemData<Snapshot>? selection)
    {
        if (ReferenceEquals(_selection, selection))
            return;

        var old = _selection;
        _selection = selection;
        SelectionChanged?.Invoke(old, _selection);
    }

    public override StringU8 DisplayString(in IFileSystemData<Snapshot> value)
        => new(value.Value.Name);

    public override string FilterString(in IFileSystemData<Snapshot> value)
        => value.Value.Name;

    public override IEnumerable<IFileSystemData<Snapshot>> GetBaseItems()
        => _generator();

    protected override bool IsSelected(SimpleCacheItem<IFileSystemData<Snapshot>> item, int globalIndex)
        => ReferenceEquals(item.Item, _selection);

    public bool Draw(string label, string preview, float width)
    {
        if (_selection != null)
        {
            if (!Draw(label, _selection, string.Empty, width, out var result))
                return false;

            SetSelection(result);
            return true;
        }

        if (!base.Draw(label, preview, string.Empty, width, out var resultItem))
            return false;

        SetSelection(resultItem.Item);
        return true;
    }
}
