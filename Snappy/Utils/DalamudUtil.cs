using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;

namespace Snappy.Utils
{
    public class DalamudUtil : IDisposable
    {
        public DalamudUtil() { }

        public void Dispose() { }

        public IGameObject? CreateGameObject(IntPtr reference)
        {
            return Svc.Objects.CreateObjectReference(reference);
        }

        public bool IsObjectPresent(IGameObject? obj)
        {
            return obj != null && obj.IsValid();
        }

        public List<IPlayerCharacter> GetPlayerCharacters()
        {
            return Svc
                .Objects.Where(obj =>
                    obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player
                    && !string.Equals(
                        obj.Name.ToString(),
                        ECommons.GameHelpers.Player.Name,
                        StringComparison.Ordinal
                    )
                )
                .Select(p => (IPlayerCharacter)p)
                .ToList();
        }

        public ICharacter? GetCharacterFromObjectTableByIndex(int index)
        {
            var objTableObj = Svc.Objects[index];
            if (objTableObj?.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player)
                return null;
            return (ICharacter)objTableObj;
        }

        public int? GetIndexFromObjectTableByName(string characterName)
        {
            for (int i = 0; i < Svc.Objects.Length; i++)
            {
                if (Svc.Objects[i] == null)
                    continue;
                if (
                    Svc.Objects[i]!.ObjectKind
                    != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player
                )
                    continue;
                if (
                    string.Equals(
                        Svc.Objects[i]!.Name.ToString(),
                        characterName,
                        StringComparison.Ordinal
                    )
                )
                    return i;
            }

            return null;
        }

        public async Task<T> RunOnFrameworkThread<T>(Func<T> func)
        {
            return await Svc.Framework.RunOnFrameworkThread(func).ConfigureAwait(false);
        }
    }
}
