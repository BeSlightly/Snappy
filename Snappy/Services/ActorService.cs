using Dalamud.Game.ClientState.Objects.SubKinds;
using ECommons.GameHelpers;
using ECommons.Reflection;
using Snappy.Common;
using Snappy.Common.Utilities;
using Penumbra.GameData.Structs;

namespace Snappy.Services;

public class ActorService : IActorService
{
    private const int GPoseActorCount = 42;

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
                 i < ObjectIndex.GPosePlayer.Index + GPoseActorCount;
                 ++i)
            {
                var p = CharacterFactory.Convert(Svc.Objects[i]);
                if (p != null && p.IsValid()) gposeActors.Add(p);
            }

            return gposeActors;
        }

        var localPlayer = Player.Object is IPlayerCharacter player
            ? new[] { player }
            : Enumerable.Empty<IPlayerCharacter>();
        var marePlayers = _ipcManager.GetMarePairedPlayers()
            .OfType<IPlayerCharacter>()
            .Where(p => p.IsValid());

        IEnumerable<ICharacter> selectableActors = localPlayer
            .Cast<ICharacter>()
            .UnionBy(marePlayers.Cast<ICharacter>(), p => p.Address);

        if (_configuration.UseLiveSnapshotData && _configuration.IncludeVisibleTempCollectionActors)
        {
            var tempCollectionActors = Svc.Objects
                .OfType<IPlayerCharacter>()
                .Where(c => c.IsValid() && IsActorVisible(c))
                .Where(c => _ipcManager.PenumbraHasTemporaryCollection(c.ObjectIndex));

            selectableActors = selectableActors.UnionBy(tempCollectionActors, p => p.Address);
        }

        if (_configuration.AllowOutsideGpose && _configuration.AllowOutsideGposeOwnedPets)
        {
            var ownedPets = Svc.Objects
                .OfType<ICharacter>()
                .Where(c => c.IsValid() && ActorOwnershipUtil.IsSelfOwnedPet(c));

            selectableActors = selectableActors.UnionBy(ownedPets, p => p.Address);
        }

        var localAddress = Player.Object?.Address ?? IntPtr.Zero;
        var allowOwnedPets = _configuration.AllowOutsideGpose && _configuration.AllowOutsideGposeOwnedPets;

        return selectableActors
            .OrderBy(p => GetActorSortKey(p, localAddress, allowOwnedPets))
            .ThenBy(p => p.Name.ToString(), StringComparer.OrdinalIgnoreCase)
            .Select(p => (ICharacter)p)
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

    private static int GetActorSortKey(ICharacter actor, IntPtr localAddress, bool allowOwnedPets)
    {
        if (actor.Address == localAddress)
            return 0;
        if (allowOwnedPets && ActorOwnershipUtil.IsSelfOwnedPet(actor))
            return 1;
        return 2;
    }
}
