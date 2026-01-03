using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VepMod.VepFramework;

// ReSharper disable Unity.NoNullCoalescing

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

namespace VepMod.Enemies.Whispral;

/// <summary>
///     Gère les hallucinations de joueurs pour le joueur local affecté par le Whispral.
///     Crée une hallucination pour chaque joueur rendu invisible.
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

        // Synchroniser les hallucinations avec les joueurs cachés
        SyncHallucinations();
    }

    private void OnDestroy()
    {
        DestroyAllHallucinations();
    }

    /// <summary>
    ///     Active ou désactive les hallucinations.
    /// </summary>
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

    /// <summary>
    ///     Retourne le HallucinationDroid correspondant au nom du joueur, ou null.
    /// </summary>
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

        // Vérifier que le prefab LostDroid est disponible
        if (!DroidPrefabLoader.IsAvailable)
        {
            LOG.Warning("LostDroid prefab not available - cannot create hallucination");
            return;
        }

        // Utiliser une position pré-calculée ou fallback sync
        var spawnPos = FindSpawnPosition();
        if (!spawnPos.HasValue)
        {
            LOG.Warning($"Could not find spawn position for hallucination of {sourcePlayer.playerName}");
            return;
        }

        // Créer l'hallucination en utilisant le prefab LostDroid
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
        // Utiliser une position pré-calculée si disponible (pas de freeze)
        if (_debuffManager != null)
        {
            var precomputed = _debuffManager.GetPrecomputedSpawnPosition();
            if (precomputed.HasValue)
            {
                LOG.Debug($"Using precomputed spawn position: {precomputed.Value}");
                return precomputed.Value;
            }
        }

        // Fallback: recherche synchrone (peut causer un freeze)
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

        // Ajouter les hallucinations manquantes
        foreach (var player in hiddenPlayers)
        {
            if (player && !_droids.ContainsKey(player))
            {
                CreateHallucination(player);
            }
        }

        // Supprimer les hallucinations pour les joueurs qui ne sont plus cachés ou dont l'hallucination a été détruite
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