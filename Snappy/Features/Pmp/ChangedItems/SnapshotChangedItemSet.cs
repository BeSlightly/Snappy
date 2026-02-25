using Penumbra.UI.Classes;

namespace Snappy.Features.Pmp.ChangedItems;

public sealed record SnapshotChangedItemCategoryGroup(
    ChangedItemIconFlag Category,
    IReadOnlyList<SnapshotChangedItem> Items
);

public sealed class SnapshotChangedItemSet
{
    public SnapshotChangedItemSet(
        IReadOnlyList<SnapshotChangedItemCategoryGroup> categories,
        IReadOnlyDictionary<string, HashSet<string>> gamePathsByItemKey)
    {
        Categories = categories;
        GamePathsByItemKey = gamePathsByItemKey;
        AllItems = categories.SelectMany(c => c.Items).ToArray();
    }

    public IReadOnlyList<SnapshotChangedItemCategoryGroup> Categories { get; }

    public IReadOnlyDictionary<string, HashSet<string>> GamePathsByItemKey { get; }

    public IReadOnlyList<SnapshotChangedItem> AllItems { get; }
}
