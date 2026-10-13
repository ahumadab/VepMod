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

    private readonly Dictionary<PlayerAvatar, Imitation> _imitations = new();
    private Coroutine? _spawning;
    private PlayerAvatar _victim = null!;

    private void Awake()
    {
        _victim = GetComponent<PlayerAvatar>();
    }

    // The game turns an Imitated Player's flashlight and map tool back on; they are hidden again after it.
    private void LateUpdate()
    {
        foreach (var (imitatedPlayer, imitation) in _imitations)
        {
            if (!imitatedPlayer) continue;

            var flashlight = imitatedPlayer.flashlightController;
            if (flashlight && (flashlight.mesh.enabled || flashlight.spotlight.enabled ||
                               (flashlight.halo && flashlight.halo.enabled)))
            {
                SetFlashlightVisible(imitatedPlayer, false);
            }

            var mapTool = imitation.MapTool;
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

            var imitation = new Imitation(mapTool);
            _imitations[player] = imitation;
            SetVisible(player, imitation, false);
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

        foreach (var (imitatedPlayer, imitation) in _imitations)
        {
            EndImitation(imitatedPlayer, imitation);
        }

        _imitations.Clear();
    }

    // Photon calls this before it destroys the leaving player's avatar, which is still in the PlayerList here.
    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        var leavingPlayer = SemiFunc.PlayerAvatarGetFromPhotonPlayer(otherPlayer);
        if (!leavingPlayer || !_imitations.TryGetValue(leavingPlayer, out var imitation)) return;

        LOG.Debug($"{leavingPlayer.playerName} left the game");
        EndImitation(leavingPlayer, imitation);
        _imitations.Remove(leavingPlayer);
    }

    public DroidController? FindHallucination(string imitatedPlayerName)
    {
        foreach (var (imitatedPlayer, imitation) in _imitations)
        {
            if (imitatedPlayer && imitation.Hallucination && imitatedPlayer.playerName == imitatedPlayerName)
            {
                return imitation.Hallucination;
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

    private void EndImitation(PlayerAvatar imitatedPlayer, Imitation imitation)
    {
        if (imitation.Hallucination)
        {
            Destroy(imitation.Hallucination!.gameObject);
        }

        if (imitatedPlayer)
        {
            SetVisible(imitatedPlayer, imitation, true);
            LOG.Debug($"{imitatedPlayer.playerName} is no longer imitated for {_victim.playerName}");
        }
    }

    // One Hallucination at a time, a frame after its spawn-point search: creating every Hallucination in the frame the
    // Victim became Haunted froze the game.
    private IEnumerator SpawnHallucinations(List<PlayerAvatar> imitatedPlayers)
    {
        foreach (var imitatedPlayer in imitatedPlayers)
        {
            while (imitatedPlayer && _imitations.TryGetValue(imitatedPlayer, out var imitation))
            {
                var spawnPosition = FindSpawnPosition();
                yield return null;

                // The Imitated Player may have left the game during that frame.
                if (!_imitations.ContainsKey(imitatedPlayer)) break;

                var hallucination = spawnPosition.HasValue
                    ? DroidController.Create(imitatedPlayer, spawnPosition.Value)
                    : null;
                if (hallucination)
                {
                    imitation.Hallucination = hallucination;
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

    private static void SetVisible(PlayerAvatar player, Imitation imitation, bool visible)
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

        var mapTool = imitation.MapTool;
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

    private sealed class Imitation
    {
        public readonly MapToolController? MapTool;

        // Null until it has spawned.
        public DroidController? Hallucination;

        public Imitation(MapToolController? mapTool)
        {
            MapTool = mapTool;
        }
    }
}
