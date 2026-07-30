using System.Diagnostics.CodeAnalysis;
using Snappy.Common.Utilities;

namespace Snappy.UI.Windows;

internal sealed class HistoryEntryCombo<T> : SimpleFilterCombo<T?>
    where T : HistoryEntryBase
{
    private readonly Func<IReadOnlyList<T>> _generator;
    private readonly string? _emptyLabel;
    private T? _selection;

    public HistoryEntryCombo(Func<IReadOnlyList<T>> generator, string? emptyLabel = null)
        : base(SimpleFilterType.Partwise)
    {
        _generator = generator;
        _emptyLabel = emptyLabel;
        DirtyCacheOnClosingPopup = true;
    }

    public T? Selection
        => _selection;

    public event Action<T?>? SelectionChanged;

    public void SetSelection(T? selection)
    {
        if (ReferenceEquals(_selection, selection))
            return;

        _selection = selection;
        SelectionChanged?.Invoke(_selection);
    }

    public string PreviewLabel
        => _selection != null
            ? HistoryEntryUtil.FormatEntryPreview(_selection)
            : _emptyLabel ?? string.Empty;

    public override StringU8 DisplayString(in T? value)
        => new(value != null ? HistoryEntryUtil.FormatEntryPreview(value) : _emptyLabel ?? string.Empty);

    public override string FilterString(in T? value)
        => value != null ? HistoryEntryUtil.FormatEntryPreview(value) : _emptyLabel ?? string.Empty;

    public override IEnumerable<T?> GetBaseItems()
    {
        var entries = _generator();
        if (_emptyLabel != null)
            yield return null;

        for (var i = entries.Count - 1; i >= 0; i--)
            yield return entries[i];
    }

    protected override bool IsSelected(SimpleCacheItem<T?> item, int globalIndex)
        => ReferenceEquals(item.Item, _selection);

    protected override bool DrawMouseWheelHandling([NotNullWhen(true)] out SimpleCacheItem<T?>? ret)
    {
        ret = default;
        return false;
    }

    public bool Draw(string label, string preview, float width)
    {
        if (!base.Draw(label, preview, string.Empty, width, out var resultItem))
            return false;

        SetSelection(resultItem.Item);
        return true;
    }
}
