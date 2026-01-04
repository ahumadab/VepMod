using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using VepMod.VepFramework;

namespace VepMod.Enemies.Whispral;

/// <summary>
///     Gestionnaire centralisé qui track le nombre de Whisprals attachés à un joueur
///     et coordonne l'activation/désactivation des debuffs.
///     Chaque client maintient son propre compteur (synchronisé via RPC).
/// </summary>
public sealed class WhispralDebuffManager : MonoBehaviour
{
    public const float SpawnDistanceMin = 5f;
    public const float SpawnDistanceMax = 15f;

    private static readonly VepLogger LOG = VepLogger.Create<WhispralDebuffManager>();

    /// <summary>
    ///     Positions de spawn pré-calculées pour les hallucinations.
    ///     Remplies pendant GoToPlayer, consommées lors du spawn.
    /// </summary>
    private readonly Queue<Vector3> _precomputedSpawnPositions = new();

    private PlayerAvatar playerAvatar = null!;

    public int AttachedCount { get; private set; }

    public bool HasWhispralAttached => AttachedCount > 0;

    /// <summary>
    ///     Nombre de positions pré-calculées disponibles.
    /// </summary>
    public int PrecomputedPositionCount => _precomputedSpawnPositions.Count;

    private void Awake()
    {
        playerAvatar = GetComponent<PlayerAvatar>();
    }

    /// <summary>
    ///     Récupère une position de spawn pré-calculée, ou null si aucune disponible.
    /// </summary>
    public Vector3? GetPrecomputedSpawnPosition()
    {
        if (_precomputedSpawnPositions.Count > 0)
        {
            var pos = _precomputedSpawnPositions.Dequeue();
            LOG.Debug($"Using precomputed spawn position: {pos} (remaining: {_precomputedSpawnPositions.Count})");
            return pos;
        }

        return null;
    }

    /// <summary>
    ///     Pré-calcule une position de spawn pour une hallucination.
    ///     Appelé pendant GoToPlayer pour éviter les freezes lors du spawn.
    /// </summary>
    public void PrecomputeSpawnPosition()
    {
        var playerPos = playerAvatar?.transform.position ?? transform.position;

        var levelPoint = SemiFunc.LevelPointGet(playerPos, SpawnDistanceMin, SpawnDistanceMax)
                         ?? SemiFunc.LevelPointGet(playerPos, 0f, 999f);

        if (levelPoint)
        {
            _precomputedSpawnPositions.Enqueue(levelPoint.transform.position);
            LOG.Debug(
                $"Precomputed spawn position: {levelPoint.transform.position} (queue size: {_precomputedSpawnPositions.Count})");
        }
    }

    /// <summary>
    ///     Appelé quand un Whispral s'attache au joueur.
    /// </summary>
    public void RegisterAttachment()
    {
        var wasActive = AttachedCount > 0;
        AttachedCount++;
        LOG.Debug($"Whispral attached, count: {AttachedCount}");

        if (!wasActive)
        {
            OnDebuffsActivated();
        }
    }

    /// <summary>
    ///     Appelé quand un Whispral se détache du joueur.
    /// </summary>
    public void UnregisterAttachment()
    {
        AttachedCount--;
        LOG.Debug($"Whispral detached, count: {AttachedCount}");

        if (AttachedCount <= 0)
        {
            AttachedCount = 0;
            OnDebuffsDeactivated();
        }
    }

    private void OnDebuffsActivated()
    {
        LOG.Debug("First Whispral attached, enabling debuffs.");

        // En singleplayer, appliquer les deux debuffs (InvisibleDebuff skip automatiquement en solo)
        // En multiplayer, filtrer selon si c'est le joueur local ou non
        var isLocalPlayer = !SemiFunc.IsMultiplayer() ||
                            (playerAvatar && playerAvatar.photonView && playerAvatar.photonView.IsMine);

        if (isLocalPlayer)
        {
            // Le joueur affecté voit les autres comme invisibles
            var invisibleDebuff = gameObject.GetOrAddComponent<InvisiblePlayerDebuff>();
            invisibleDebuff.ApplyDebuff(true);

            // Créer des hallucinations des joueurs cachés qui se baladent au hasard
            var hallucinationDebuff = gameObject.GetOrAddComponent<DroidDebuff>();
            hallucinationDebuff.ApplyDebuff(true, invisibleDebuff);
        }
        else
        {
            // Les autres joueurs voient les pupilles agrandies du joueur affecté
            var pupilDebuff = gameObject.GetOrAddComponent<DilatedPupilsDebuff>();
            pupilDebuff.ApplyDebuff(true);
        }
    }

    private void OnDebuffsDeactivated()
    {
        LOG.Debug("Last Whispral detached, disabling debuffs.");

        var isLocalPlayer = !SemiFunc.IsMultiplayer() ||
                            (playerAvatar && playerAvatar.photonView && playerAvatar.photonView.IsMine);

        if (isLocalPlayer)
        {
            var invisibleDebuff = GetComponent<InvisiblePlayerDebuff>();
            var hallucinationDebuff = GetComponent<DroidDebuff>();

            // Désactiver les hallucinations en premier
            if (hallucinationDebuff)
            {
                hallucinationDebuff.ApplyDebuff(false, invisibleDebuff);
            }

            // Puis restaurer la visibilité des vrais joueurs
            if (invisibleDebuff)
            {
                invisibleDebuff.ApplyDebuff(false);
            }
        }
        else
        {
            var pupilDebuff = GetComponent<DilatedPupilsDebuff>();
            if (pupilDebuff)
            {
                pupilDebuff.ApplyDebuff(false);
            }
        }

        // Vider le cache des positions
        _precomputedSpawnPositions.Clear();
    }
}