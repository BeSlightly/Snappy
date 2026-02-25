using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Luna;
using Penumbra.GameData.Data;
using Penumbra.GameData.DataContainers;

namespace Snappy.Services;

public sealed class PenumbraGameDataProvider : IDisposable
{
    private readonly Logger _log;
    private readonly object _initLock = new();
    private ObjectIdentification? _identifier;
    private ItemData? _itemData;
    private readonly List<IDisposable> _disposables = new();

    public PenumbraGameDataProvider(Logger log)
    {
        _log = log;
    }

    public async Task<ObjectIdentification> GetIdentifierAsync()
    {
        EnsureInitialized();
        if (_identifier == null)
            throw new InvalidOperationException("Failed to initialize Penumbra game data.");

        await _identifier.Awaiter;
        return _identifier;
    }

    public async Task<ItemData> GetItemDataAsync()
    {
        EnsureInitialized();
        if (_itemData == null)
            throw new InvalidOperationException("Failed to initialize Penumbra item data.");

        await _itemData.Awaiter;
        return _itemData;
    }

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            try
            {
                disposable.Dispose();
            }
            catch (Exception ex)
            {
                PluginLog.Warning($"Failed to dispose Penumbra game data service: {ex.Message}");
            }
        }

        _disposables.Clear();
    }

    private void EnsureInitialized()
    {
        if (_identifier != null)
            return;

        lock (_initLock)
        {
            if (_identifier != null)
                return;

            Initialize();
        }
    }

    private void Initialize()
    {
        var pluginInterface = Svc.PluginInterface;
        var dataManager = Svc.Data;
        var evaluator = Svc.SeStringEvaluator;

        var bonusItems = Track(new DictBonusItems(pluginInterface, _log, dataManager));
        var itemsByType = Track(new ItemsByType(pluginInterface, _log, dataManager, bonusItems));
        var equipmentId = Track(new IdentificationListEquipment(pluginInterface, _log, dataManager, itemsByType));
        var weaponId = Track(new IdentificationListWeapons(pluginInterface, _log, dataManager, itemsByType));
        var modelId = Track(new IdentificationListModels(pluginInterface, dataManager, _log));
        var itemsPrimary = Track(new ItemsPrimaryModel(pluginInterface, _log, dataManager, itemsByType));
        var itemsSecondary = Track(new ItemsSecondaryModel(pluginInterface, _log, dataManager, itemsByType));
        var itemsTertiary = Track(new ItemsTertiaryModel(pluginInterface, _log, dataManager, itemsByType, itemsSecondary));
        _itemData = new ItemData(itemsByType, itemsPrimary, itemsSecondary, itemsTertiary);

        var actionDict = Track(new DictAction(pluginInterface, _log, dataManager));
        var emoteDict = Track(new DictEmote(pluginInterface, _log, dataManager));
        var bNpcNames = Track(new DictBNpcNames(pluginInterface, _log));

        var worldDict = Track(new DictWorld(pluginInterface, _log, dataManager));
        var mountDict = Track(new DictMount(pluginInterface, _log, dataManager));
        var companionDict = Track(new DictCompanion(pluginInterface, _log, dataManager, evaluator));
        var ornamentDict = Track(new DictOrnament(pluginInterface, _log, dataManager));
        var bNpcDict = Track(new DictBNpc(pluginInterface, _log, dataManager, evaluator));
        var eNpcDict = Track(new DictENpc(pluginInterface, _log, dataManager, evaluator));
        var nameDicts = new NameDicts(worldDict, mountDict, companionDict, ornamentDict, bNpcDict, eNpcDict);

        var modelCharaDict = Track(new DictModelChara(pluginInterface, _log, dataManager, bNpcNames, nameDicts));
        var parser = new GamePathParser(_log);

        _identifier = new ObjectIdentification(
            bNpcNames,
            actionDict,
            emoteDict,
            modelCharaDict,
            equipmentId,
            weaponId,
            modelId,
            parser);
    }

    private T Track<T>(T disposable) where T : IDisposable
    {
        _disposables.Add(disposable);
        return disposable;
    }
}
