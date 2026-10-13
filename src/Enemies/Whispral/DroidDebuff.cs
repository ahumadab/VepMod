using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VepMod.VepFramework;

// ReSharper disable Unity.NoNullCoalescing

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

namespace VepMod.Enemies.Whispral;

/// <summary>
///     One Hallucination per Imitated Player of the local Victim.
/// </summary>
public sealed class DroidDebuff : MonoBehaviour
{
    private const float SpawnRetryDelay = 1f;

    private static readonly VepLogger LOG = VepLogger.Create<DroidDebuff>();

    private readonly Dictionary<PlayerAvatar, DroidController> _droids = new();
    private readonly List<PlayerAvatar> _staleImitatedPlayers = new();
    private InvisiblePlayerDebuff _invisiblePlayerDebuff;
    private Coroutine? _spawning;

    public bool IsActive { get; private set; }

    private void Update()
    {
        if (!IsActive || _invisiblePlayerDebuff == null) return;

        SyncHallucinations();
    }

    private void OnDestroy()
    {
        DestroyAllHallucinations();
    }

    public void ApplyDebuff(bool active, InvisiblePlayerDebuff sourcePlayerDebuff)
    {
        if (!SemiFunc.IsMultiplayer())
        {
            LOG.Debug("Singleplayer mode detected, skipping hallucinations.");
            return;
        }

        IsActive = active;
        _invisiblePlayerDebuff = sourcePlayerDebuff;

        if (!active)
        {
            DestroyAllHallucinations();
        }
    }

    public DroidController? GetDroidByPlayerName(string playerName)
    {
        foreach (var kvp in _droids)
        {
            if (kvp.Key && kvp.Value && kvp.Key.playerName == playerName)
            {
                return kvp.Value;
            }
        }

        return null;
    }

    // One Hallucination per run, a frame after its spawn-point search: creating every Hallucination in the frame the
    // Victim became Haunted froze the game.
    private IEnumerator SpawnHallucination(PlayerAvatar imitatedPlayer)
    {
        var spawnPosition = FindSpawnPosition();
        yield return null;

        var hallucination = imitatedPlayer && spawnPosition.HasValue
            ? DroidController.Create(imitatedPlayer, spawnPosition.Value)
            : null;

        if (hallucination == null)
        {
            LOG.Warning("Hallucination failed to spawn, retrying later");
            yield return new WaitForSeconds(SpawnRetryDelay);
        }
        else
        {
            _droids[imitatedPlayer] = hallucination;
            var distance = Vector3.Distance(spawnPosition!.Value, PlayerAvatar.instance.transform.position);
            LOG.Debug($"Hallucination of {imitatedPlayer.playerName} spawned {distance:F1} m from the Victim");
        }

        _spawning = null;
    }

    private void DestroyAllHallucinations()
    {
        LOG.Debug("Destroying all hallucinations.");

        if (_spawning != null)
        {
            StopCoroutine(_spawning);
            _spawning = null;
        }

        foreach (var kvp in _droids)
        {
            if (kvp.Value)
            {
                Destroy(kvp.Value.gameObject);
            }
        }

        _droids.Clear();
    }

    private void DestroyHallucination(PlayerAvatar? player)
    {
        if (player == null || !_droids.TryGetValue(player, out var hallucination)) return;

        LOG.Debug($"Destroying hallucination for player {player.playerName}");

        if (hallucination)
        {
            Destroy(hallucination.gameObject);
        }

        _droids.Remove(player);
    }

    private static Vector3? FindSpawnPosition()
    {
        var victimPosition = PlayerAvatar.instance.transform.position;

        var levelPoint = SemiFunc.LevelPointGet(victimPosition, DroidController.SpawnDistanceMin,
                             DroidController.SpawnDistanceMax)
                         ?? SemiFunc.LevelPointGet(victimPosition, 0f, 999f);

        if (!levelPoint)
        {
            LOG.Warning("No LevelPoint found for hallucination spawn");
            return null;
        }

        return levelPoint.transform.position;
    }

    private void SyncHallucinations()
    {
        var hiddenPlayers = _invisiblePlayerDebuff.HiddenPlayers;

        if (_spawning == null)
        {
            for (var i = 0; i < hiddenPlayers.Count; i++)
            {
                var player = hiddenPlayers[i];
                if (player && !_droids.ContainsKey(player))
                {
                    _spawning = StartCoroutine(SpawnHallucination(player));
                    break;
                }
            }
        }

        foreach (var kvp in _droids)
        {
            if (!kvp.Key || !kvp.Value || !hiddenPlayers.Contains(kvp.Key))
            {
                _staleImitatedPlayers.Add(kvp.Key);
            }
        }

        foreach (var player in _staleImitatedPlayers)
        {
            DestroyHallucination(player);
        }

        _staleImitatedPlayers.Clear();
    }
}
