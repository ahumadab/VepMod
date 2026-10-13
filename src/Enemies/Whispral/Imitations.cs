using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using VepMod.VepFramework;

namespace VepMod.Enemies.Whispral;

/// <summary>
///     Vanish and Hallucinations of one Victim, on the Victim's client: one Imitation per Imitated Player, from
///     <see cref="Begin" /> to <see cref="End" />.
/// </summary>
public sealed class Imitations : MonoBehaviourPunCallbacks
{
    private const float SpawnRetryDelay = 1f; // s
    private const float FootstepMuteRadius = 2f; // m

    private static readonly VepLogger LOG = VepLogger.Create<Imitations>();

    // An Imitated Player maps to null until their Hallucination has spawned.
    private readonly Dictionary<PlayerAvatar, DroidController?> _imitations = new();
    private readonly Dictionary<PlayerAvatar, MapToolController?> _mapTools = new();
    private Coroutine? _spawning;
    private PlayerAvatar _victim = null!;

    private void Awake()
    {
        _victim = GetComponent<PlayerAvatar>();
    }

    // The game turns a hidden player's flashlight and map tool back on; they are hidden again after it.
    private void LateUpdate()
    {
        foreach (var imitatedPlayer in _imitations.Keys)
        {
            if (!imitatedPlayer) continue;

            var flashlight = imitatedPlayer.flashlightController;
            if (flashlight && (flashlight.mesh.enabled || flashlight.spotlight.enabled ||
                               (flashlight.halo && flashlight.halo.enabled)))
            {
                SetFlashlightVisible(imitatedPlayer, false);
            }

            var mapTool = _mapTools[imitatedPlayer];
            if (mapTool && mapTool!.VisualTransform && mapTool.VisualTransform.gameObject.activeSelf)
            {
                mapTool.VisualTransform.gameObject.SetActive(false);
            }
        }
    }

    private void OnDestroy()
    {
        End();
    }

    public void Begin()
    {
        // A MapToolController is not under its PlayerAvatar in the hierarchy: it is found in the scene by its
        // PlayerAvatar property.
        var sceneMapTools = FindObjectsOfType<MapToolController>(true);

        foreach (var player in GameDirector.instance.PlayerList)
        {
            if (!player || player == _victim) continue;

            MapToolController? mapTool = null;
            foreach (var candidate in sceneMapTools)
            {
                if (candidate.PlayerAvatar == player) mapTool = candidate;
            }

            _imitations[player] = null;
            _mapTools[player] = mapTool;
            SetVisible(player, false);
            LOG.Debug($"{player.playerName} is imitated for {_victim.playerName}");
        }

        _spawning = StartCoroutine(SpawnHallucinations(new List<PlayerAvatar>(_imitations.Keys)));
    }

    public void End()
    {
        if (_spawning != null)
        {
            StopCoroutine(_spawning);
            _spawning = null;
        }

        foreach (var imitation in _imitations)
        {
            EndImitation(imitation.Key, imitation.Value);
        }

        _imitations.Clear();
        _mapTools.Clear();
    }

    // Photon calls this before it destroys the leaving player's avatar, which is still in the PlayerList here.
    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        var leavingPlayer = SemiFunc.PlayerAvatarGetFromPhotonPlayer(otherPlayer);
        if (!leavingPlayer || !_imitations.TryGetValue(leavingPlayer, out var hallucination)) return;

        LOG.Debug($"{leavingPlayer.playerName} left the game");
        EndImitation(leavingPlayer, hallucination);
        _imitations.Remove(leavingPlayer);
        _mapTools.Remove(leavingPlayer);
    }

    public DroidController? FindHallucination(string imitatedPlayerName)
    {
        foreach (var imitation in _imitations)
        {
            if (imitation.Key && imitation.Value && imitation.Key.playerName == imitatedPlayerName)
            {
                return imitation.Value;
            }
        }

        return null;
    }

    public bool IsNearImitatedPlayer(Vector3 position)
    {
        foreach (var imitatedPlayer in _imitations.Keys)
        {
            if (imitatedPlayer && Vector3.Distance(position, imitatedPlayer.transform.position) <= FootstepMuteRadius)
            {
                return true;
            }
        }

        return false;
    }

    private void EndImitation(PlayerAvatar imitatedPlayer, DroidController? hallucination)
    {
        if (hallucination)
        {
            Destroy(hallucination!.gameObject);
        }

        if (imitatedPlayer)
        {
            SetVisible(imitatedPlayer, true);
            LOG.Debug($"{imitatedPlayer.playerName} is no longer imitated for {_victim.playerName}");
        }
    }

    // One Hallucination at a time, a frame after its spawn-point search: creating every Hallucination in the frame the
    // Victim became Haunted froze the game.
    private IEnumerator SpawnHallucinations(List<PlayerAvatar> imitatedPlayers)
    {
        foreach (var imitatedPlayer in imitatedPlayers)
        {
            while (_imitations.ContainsKey(imitatedPlayer))
            {
                var spawnPosition = FindSpawnPosition();
                yield return null;

                if (!_imitations.ContainsKey(imitatedPlayer)) break;

                var hallucination = spawnPosition.HasValue
                    ? DroidController.Create(imitatedPlayer, spawnPosition.Value)
                    : null;
                if (hallucination)
                {
                    _imitations[imitatedPlayer] = hallucination;
                    var distance = Vector3.Distance(spawnPosition!.Value, _victim.transform.position);
                    LOG.Debug($"Hallucination of {imitatedPlayer.playerName} spawned {distance:F1} m from the Victim");
                    break;
                }

                LOG.Warning($"Hallucination of {imitatedPlayer.playerName} failed to spawn, retrying");
                yield return new WaitForSeconds(SpawnRetryDelay);
            }
        }

        _spawning = null;
    }

    private Vector3? FindSpawnPosition()
    {
        var victimPosition = _victim.transform.position;

        var levelPoint = SemiFunc.LevelPointGet(victimPosition, DroidController.SpawnDistanceMin,
            DroidController.SpawnDistanceMax);
        if (!levelPoint)
        {
            levelPoint = SemiFunc.LevelPointGet(victimPosition, 0f, 999f);
        }

        if (!levelPoint)
        {
            LOG.Warning("No LevelPoint found for a Hallucination spawn");
            return null;
        }

        return levelPoint.transform.position;
    }

    private void SetVisible(PlayerAvatar player, bool visible)
    {
        if (player.playerAvatarVisuals && player.playerAvatarVisuals.meshParent)
        {
            player.playerAvatarVisuals.meshParent.SetActive(visible);
        }

        if (player.worldSpaceUIPlayerName)
        {
            player.worldSpaceUIPlayerName.gameObject.SetActive(visible);
        }

        SetFlashlightVisible(player, visible);

        if (player.voiceChat && player.voiceChat.audioSource)
        {
            player.voiceChat.audioSource.mute = !visible;
        }

        var mapTool = _mapTools[player];
        if (mapTool && mapTool!.VisualTransform)
        {
            mapTool.VisualTransform.gameObject.SetActive(visible);
        }
    }

    private static void SetFlashlightVisible(PlayerAvatar player, bool visible)
    {
        var flashlight = player.flashlightController;
        if (!flashlight) return;

        flashlight.spotlight.enabled = visible;
        flashlight.mesh.enabled = visible;
        if (flashlight.halo)
        {
            flashlight.halo.enabled = visible;
        }
    }
}
