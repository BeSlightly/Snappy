using Penumbra.UI;

namespace Snappy.Features.Pmp.ChangedItems;

public sealed record SnapshotChangedItem(
    string Key,
    string Name,
    string AdditionalData,
    int Count,
    ChangedItemIconFlag Category
);
