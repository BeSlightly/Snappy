using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ImGuiNET;
using OtterGui.Raii;
using OtterGui.Text;
using Snappy.Utils;

namespace Snappy.UI
{
    public partial class MainWindow
    {
        public const int GPoseObjectId = 201;
        public const int CharacterScreenIndex = 240;
        public const int ExamineScreenIndex = 241;
        public const int FittingRoomIndex = 242;
        public const int DyePreviewIndex = 243;

        private string playerFilter = string.Empty;
        private string playerFilterLower = string.Empty;
        private string currentLabel = string.Empty;
        private ICharacter? player;
        private int? objIdxSelected;
        private bool _isActorSnapshottable = false;
        private bool _snapshotExistsForActor = false;
        private bool _isActorModifiable = false;
        private readonly List<ICharacter> _sortedActorList = new();
        private readonly Stopwatch _actorListRefreshStopwatch = Stopwatch.StartNew();
        private const int ActorListRefreshIntervalMs = 2000; // Refresh every 2 seconds.

        private void ClearSelectedActorState()
        {
            player = null;
            currentLabel = string.Empty;
            objIdxSelected = null;

            _isActorSnapshottable = false;
            _snapshotExistsForActor = false;
            _isActorModifiable = false;
        }

        private void UpdateSelectedActorState()
        {
            if (player == null || objIdxSelected == null)
            {
                ClearSelectedActorState();
                return;
            }

            // --- Update modifiable state ---
            var inGpose = Svc.Objects[201] != null;
            if (inGpose)
            {
                _isActorModifiable = true;
            }
            else
            {
                var isLocalPlayer = player.ObjectIndex == Player.Object?.ObjectIndex;
                _isActorModifiable =
                    isLocalPlayer
                    && Plugin.Configuration.DisableAutomaticRevert
                    && Plugin.Configuration.AllowOutsideGpose;
            }

            // --- Update snapshottable state ---
            _snapshotExistsForActor =
                Plugin.SnapshotManager.FindSnapshotPathForActor(player) != null;

            if (inGpose)
            {
                _isActorSnapshottable = false;
            }
            else
            {
                var isSelf = string.Equals(
                    player.Name.TextValue,
                    Player.Name,
                    StringComparison.Ordinal
                );
                // This uses a 5-second cache internally.
                var isMarePaired = Plugin
                    .IpcManager.GetMarePairedPlayers()
                    .Any(p => p.Address == player.Address);
                _isActorSnapshottable = isSelf || isMarePaired;
            }
        }

        private void RefreshSortedActorList()
        {
            _sortedActorList.Clear();
            var uniqueActors = new Dictionary<IntPtr, ICharacter>();

            // Add self
            if (Player.Available)
            {
                uniqueActors[Player.Object.Address] = Player.Object;
            }

            // Add Mare players
            var marePlayers = Plugin.IpcManager.GetMarePairedPlayers();
            foreach (var marePlayer in marePlayers)
            {
                if (marePlayer.IsValid())
                {
                    uniqueActors[marePlayer.Address] = marePlayer;
                }
            }

            _sortedActorList.AddRange(uniqueActors.Values);

            // Local player always on top, the rest alphabetical.
            _sortedActorList.Sort(
                (a, b) =>
                {
                    bool isALocalPlayer = Player.Available && a.Address == Player.Object.Address;
                    bool isBLocalPlayer = Player.Available && b.Address == Player.Object.Address;

                    if (isALocalPlayer && !isBLocalPlayer)
                        return -1; // a (local player) comes first
                    if (!isALocalPlayer && isBLocalPlayer)
                        return 1; // b (local player) comes first

                    // Otherwise, sort alphabetically
                    return string.Compare(
                        a.Name.ToString(),
                        b.Name.ToString(),
                        StringComparison.Ordinal
                    );
                }
            );
        }

        private void DrawPlayerFilter()
        {
            var width = ImGui.GetContentRegionAvail().X;
            ImGui.SetNextItemWidth(width);
            if (ImUtf8.InputText("##playerFilter", ref playerFilter, "Filter Players..."))
                playerFilterLower = playerFilter.ToLowerInvariant();
        }

        private void DrawGPoseSelectable(ICharacter gposePlayer, int objIdx)
        {
            var playerName = gposePlayer.Name.ToString();
            if (!playerName.Any())
                return;

            DrawSelectable(gposePlayer, $"{playerName} (GPose)", objIdx);
        }

        private void DrawSelectable(ICharacter selectablePlayer, string label, int objIdx)
        {
            if (playerFilterLower.Any() && !label.ToLowerInvariant().Contains(playerFilterLower))
                return;

            bool isSelected = currentLabel == label;
            if (ImUtf8.Selectable(label, isSelected))
            {
                if (isSelected)
                {
                    ClearSelectedActorState();
                }
                else
                {
                    currentLabel = label;
                    this.player = selectablePlayer;
                    this.objIdxSelected = objIdx;
                    UpdateSelectedActorState();
                }
            }
        }

        private void DrawPlayerSelectable(ICharacter playerToDraw)
        {
            var playerName = playerToDraw.Name.ToString();
            if (!playerName.Any())
                return;

            var label = GetLabel(playerToDraw, playerName);
            DrawSelectable(playerToDraw, label, playerToDraw.ObjectIndex);
        }

        private static string GetLabel(ICharacter player, string playerName)
        {
            if (player.ObjectKind == ObjectKind.Player)
                return playerName;

            if (player.ModelType() == 0)
                return $"{playerName} (NPC)";

            return $"{playerName} (Monster)";
        }

        private (string Text, string Tooltip, bool IsDisabled) GetSnapshotButtonState()
        {
            if (player == null)
            {
                return ("Save Snapshot", "Select an actor to save or update its snapshot.", true);
            }

            if (Svc.Objects[201] != null) // In Gpose
            {
                var buttonText = _snapshotExistsForActor ? "Update Snapshot" : "Save Snapshot";
                return (
                    buttonText,
                    "Saving or updating snapshots is unavailable while in GPose.",
                    true
                );
            }

            if (!_isActorSnapshottable)
            {
                return (
                    "Save Snapshot",
                    "Can only save snapshots of yourself, or players you are paired with in Mare Synchronos.",
                    true
                );
            }

            if (_snapshotExistsForActor)
            {
                return (
                    "Update Snapshot",
                    $"Update existing snapshot for {player.Name.TextValue}.\n(Folder can be renamed freely)",
                    false
                );
            }

            return ("Save Snapshot", $"Save a new snapshot for {player.Name.TextValue}.", false);
        }

        private void DrawPlayerSelector()
        {
            if (_actorListRefreshStopwatch.ElapsedMilliseconds > ActorListRefreshIntervalMs)
            {
                RefreshSortedActorList();
                _actorListRefreshStopwatch.Restart();
            }

            ImGui.BeginGroup();
            DrawPlayerFilter();

            var buttonHeight = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.Y;
            var listHeight = ImGui.GetContentRegionAvail().Y - buttonHeight;

            using (
                var child = ImRaii.Child(
                    "ActorList",
                    new System.Numerics.Vector2(ImGui.GetContentRegionAvail().X, listHeight),
                    false
                )
            )
            {
                if (child)
                {
                    if (Svc.Objects[201] != null)
                    {
                        for (var i = GPoseObjectId; i < GPoseObjectId + 48; ++i)
                        {
                            var p = CharacterFactory.Convert(Svc.Objects[i]);
                            if (p == null)
                                break;
                            DrawGPoseSelectable(p, i);
                        }
                    }
                    else
                    {
                        foreach (var actor in _sortedActorList)
                        {
                            DrawPlayerSelectable(actor);
                        }
                    }
                }
            }

            var (buttonText, tooltipText, isButtonDisabled) = GetSnapshotButtonState();

            if (
                ImUtf8.ButtonEx(
                    buttonText,
                    tooltipText,
                    new System.Numerics.Vector2(ImGui.GetContentRegionAvail().X, 0),
                    isButtonDisabled
                )
            )
            {
                var updatedSnapshotPath = Plugin.SnapshotManager.UpdateSnapshot(player!);
                if (updatedSnapshotPath != null)
                {
                    Plugin.InvokeSnapshotsUpdated();
                    if (
                        _selectedSnapshot != null
                        && string.Equals(
                            updatedSnapshotPath,
                            _selectedSnapshot.FullName,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        LoadHistoryForSelectedSnapshot();
                    }
                }
            }

            ImGui.EndGroup();
        }
    }
}
