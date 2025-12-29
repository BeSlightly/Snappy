using ECommons.GameHelpers;
using ECommons.Reflection;
using Snappy.Common;
using Penumbra.GameData.Structs;

namespace Snappy.Services;

public class ActorService : IActorService
{
    private readonly IIpcManager _ipcManager;
    private readonly Configuration _configuration;

    public ActorService(IIpcManager ipcManager, Configuration configuration)
    {
        _ipcManager = ipcManager;
        _configuration = configuration;
    }

    public List<ICharacter> GetSelectableActors()
    {
        if (PluginUtil.IsInGpose())
        {
            var gposeActors = new List<ICharacter>();
            for (var i = ObjectIndex.GPosePlayer.Index;
                 i < ObjectIndex.GPosePlayer.Index + 42;
                 ++i)
            {
                var p = CharacterFactory.Convert(Svc.Objects[i]);
                if (p != null && p.IsValid()) gposeActors.Add(p);
            }

            return gposeActors;
        }

        var localPlayer = Player.Object is { } player
            ? new[] { player }
            : Enumerable.Empty<ICharacter>();
        var marePlayers = _ipcManager.GetMarePairedPlayers().Where(p => p.IsValid());

        var selectableActors = localPlayer.UnionBy(marePlayers, p => p.Address);

        if (_configuration.UseLiveSnapshotData && _configuration.IncludeVisibleTempCollectionActors)
        {
            var tempCollectionActors = Svc.Objects
                .OfType<ICharacter>()
                .Where(c => c.IsValid() && IsActorVisible(c))
                .Where(c => _ipcManager.PenumbraHasTemporaryCollection(c.ObjectIndex));

            selectableActors = selectableActors.UnionBy(tempCollectionActors, p => p.Address);
        }

        return selectableActors
            .OrderBy(p => p.Address != (Player.Object?.Address ?? IntPtr.Zero))
            .ThenBy(p => p.Name.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsActorVisible(IGameObject actor)
    {
        var visibleObj = actor.GetFoP("IsVisible");
        if (visibleObj is bool isVisible) return isVisible;

        var targetableObj = actor.GetFoP("IsTargetable");
        if (targetableObj is bool isTargetable) return isTargetable;

        return true;
    }
}
