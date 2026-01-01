using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VepMod.VepFramework;

namespace VepMod.Enemies.Whispral;

/// <summary>
///     Gère les hallucinations de joueurs pour le joueur local affecté par le Whispral.
///     Crée une hallucination pour chaque joueur rendu invisible.
/// </summary>
public sealed class HallucinationDebuff : MonoBehaviour
{
    private const float NavMeshSampleRadius = 10f;
    private const float SpawnDistanceMin = 5f;
    private const float SpawnDistanceMax = 15f;
    private static readonly VepLogger LOG = VepLogger.Create<HallucinationDebuff>(true);

    private readonly Dictionary<PlayerAvatar, HallucinationDroid> hallucinations = new();

    private InvisibleDebuff invisibleDebuff;

    public bool IsActive { get; private set; }

    private void Update()
    {
        if (!IsActive || invisibleDebuff == null) return;

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
    public void ApplyDebuff(bool active, InvisibleDebuff sourceDebuff)
    {
        if (!SemiFunc.IsMultiplayer())
        {
            LOG.Debug("Singleplayer mode detected, skipping hallucinations.");
            return;
        }

        IsActive = active;
        invisibleDebuff = sourceDebuff;

        if (active)
        {
            CreateHallucinations();
        }
        else
        {
            DestroyAllHallucinations();
        }
    }

    private void CreateHallucination(PlayerAvatar sourcePlayer)
    {
        LOG.Debug($"Creating hallucination for player {sourcePlayer.playerName}");

        // Vérifier que le prefab LostDroid est disponible
        if (!LostDroidPrefabLoader.IsAvailable)
        {
            LOG.Warning("LostDroid prefab not available - cannot create hallucination");
            return;
        }

        // Trouver une position de spawn aléatoire sur le NavMesh
        var spawnPos = FindRandomSpawnPosition();
        if (!spawnPos.HasValue)
        {
            LOG.Warning($"Could not find spawn position for hallucination of {sourcePlayer.playerName}");
            return;
        }

        // Créer l'hallucination en utilisant le prefab LostDroid
        var hallucination = HallucinationDroid.Create(sourcePlayer, spawnPos.Value);
        if (hallucination == null)
        {
            LOG.Warning($"Hallucination for {sourcePlayer.playerName} failed to create");
            return;
        }

        hallucinations[sourcePlayer] = hallucination;
        LOG.Debug($"Hallucination created at {hallucination.transform.position}");
    }

    private void CreateHallucinations()
    {
        if (invisibleDebuff == null) return;

        LOG.Debug("Creating hallucinations for hidden players.");

        foreach (var player in invisibleDebuff.HiddenPlayers)
        {
            if (player && !hallucinations.ContainsKey(player))
            {
                CreateHallucination(player);
            }
        }
    }

    private void DestroyAllHallucinations()
    {
        LOG.Debug("Destroying all hallucinations.");

        foreach (var kvp in hallucinations)
        {
            if (kvp.Value)
            {
                Destroy(kvp.Value.gameObject);
            }
        }

        hallucinations.Clear();
    }

    private void DestroyHallucination(PlayerAvatar player)
    {
        if (!hallucinations.TryGetValue(player, out var hallucination)) return;

        LOG.Debug($"Destroying hallucination for player {player?.playerName ?? "unknown"}");

        if (hallucination)
        {
            Destroy(hallucination.gameObject);
        }

        hallucinations.Remove(player);
    }

    private Vector3? FindRandomSpawnPosition()
    {
        var playerPos = PlayerAvatar.instance?.transform.position ?? transform.position;

        // Utiliser les LevelPoints qui sont garantis d'être sur le NavMesh (comme les ennemis)
        var levelPoint = SemiFunc.LevelPointGet(playerPos, SpawnDistanceMin, SpawnDistanceMax)
                         ?? SemiFunc.LevelPointGet(playerPos, 0f, 999f);

        if (!levelPoint)
        {
            LOG.Warning("No LevelPoint found for hallucination spawn");
            return null;
        }

        // Utiliser directement la position du LevelPoint (elle est garantie d'être valide)
        LOG.Debug($"Using LevelPoint position: {levelPoint.transform.position}");
        return levelPoint.transform.position;
    }

    private void SyncHallucinations()
    {
        var hiddenPlayers = invisibleDebuff.HiddenPlayers;

        // Ajouter les hallucinations manquantes
        foreach (var player in hiddenPlayers)
        {
            if (player && !hallucinations.ContainsKey(player))
            {
                CreateHallucination(player);
            }
        }

        // Supprimer les hallucinations pour les joueurs qui ne sont plus cachés ou dont l'hallucination a été détruite
        var toRemove = new List<PlayerAvatar>();
        foreach (var kvp in hallucinations)
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