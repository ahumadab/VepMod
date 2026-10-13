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
    private static readonly VepLogger LOG = VepLogger.Create<DroidDebuff>();

    private readonly Dictionary<PlayerAvatar, DroidController> _droids = new();
    private WhispralDebuffManager _debuffManager;
    private InvisiblePlayerDebuff _invisiblePlayerDebuff;

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
        _debuffManager = GetComponent<WhispralDebuffManager>();

        if (active)
        {
            CreateHallucinations();
        }
        else
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

    private void CreateHallucination(PlayerAvatar sourcePlayer)
    {
        LOG.Debug($"Creating hallucination for player {sourcePlayer.playerName}");

        if (!DroidPrefabLoader.IsAvailable)
        {
            LOG.Warning("Hallucination prefab not available - cannot create hallucination");
            return;
        }

        var spawnPos = FindSpawnPosition();
        if (!spawnPos.HasValue)
        {
            LOG.Warning($"Could not find spawn position for hallucination of {sourcePlayer.playerName}");
            return;
        }

        var hallucination = DroidController.Create(sourcePlayer, spawnPos.Value);
        if (hallucination == null)
        {
            LOG.Warning($"Hallucination for {sourcePlayer.playerName} failed to create");
            return;
        }

        _droids[sourcePlayer] = hallucination;
        LOG.Debug($"Hallucination created at {hallucination.transform.position}");
    }

    private void CreateHallucinations()
    {
        if (_invisiblePlayerDebuff == null) return;

        LOG.Debug("Creating hallucinations for hidden players.");

        foreach (var player in _invisiblePlayerDebuff.HiddenPlayers)
        {
            if (player && !_droids.ContainsKey(player))
            {
                CreateHallucination(player);
            }
        }
    }

    private void DestroyAllHallucinations()
    {
        LOG.Debug("Destroying all hallucinations.");

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

    private Vector3? FindSpawnPosition()
    {
        // A precomputed position avoids the freeze of the synchronous search.
        if (_debuffManager != null)
        {
            var precomputed = _debuffManager.GetPrecomputedSpawnPosition();
            if (precomputed.HasValue)
            {
                LOG.Debug($"Using precomputed spawn position: {precomputed.Value}");
                return precomputed.Value;
            }
        }

        // Synchronous search, which may freeze the game.
        LOG.Warning("No precomputed position available, falling back to sync search");
        var playerPos = PlayerAvatar.instance.transform.position;

        var levelPoint = SemiFunc.LevelPointGet(playerPos, WhispralDebuffManager.SpawnDistanceMin,
                             WhispralDebuffManager.SpawnDistanceMax)
                         ?? SemiFunc.LevelPointGet(playerPos, 0f, 999f);

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

        foreach (var player in hiddenPlayers)
        {
            if (player && !_droids.ContainsKey(player))
            {
                CreateHallucination(player);
            }
        }

        var toRemove = new List<PlayerAvatar>();
        foreach (var kvp in _droids)
        {
            if (!kvp.Key || !kvp.Value || !hiddenPlayers.Contains(kvp.Key))
            {
                toRemove.Add(kvp.Key);
            }
        }

        foreach (var player in toRemove)
        {
            DestroyHallucination(player);
        }
    }
}
