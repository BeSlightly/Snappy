namespace Snappy.Features.Pmp.ChangedItems;

public interface ISnapshotChangedItemService
{
    Task<SnapshotChangedItemSet> BuildChangedItemsAsync(IEnumerable<string> gamePaths, string? base64Manipulations,
        IReadOnlyDictionary<string, string>? resolvedFileMap = null, string? filesDirectory = null);

    Task<string> FilterManipulationsAsync(string base64Manipulations, IReadOnlySet<string> selectedItemKeys);

    Task<IReadOnlySet<string>> GetEquippedItemKeysAsync(string? glamourerBase64);

    Task<GlamourerCustomizationFilter?> GetCustomizationFilterAsync(string? glamourerBase64);

    Task<IReadOnlySet<string>> GetCustomizationKeysFromManipulationsAsync(string? base64Manipulations,
        GlamourerCustomizationFilter? customizationFilter = null);

    SnapshotChangedItemSet FilterToItemKeys(SnapshotChangedItemSet items, IReadOnlySet<string> allowedKeys,
        GlamourerCustomizationFilter? customizationFilter,
        IReadOnlySet<string>? customizationOverrides);
}
