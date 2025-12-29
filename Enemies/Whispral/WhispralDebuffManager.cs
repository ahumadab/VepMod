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
    private static readonly VepLogger LOG = VepLogger.Create<WhispralDebuffManager>();

    private PlayerAvatar playerAvatar;

    public int AttachedCount { get; private set; }

    public bool HasWhispralAttached => AttachedCount > 0;

    private void Awake()
    {
        playerAvatar = GetComponent<PlayerAvatar>();
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
            var invisibleDebuff = gameObject.GetOrAddComponent<InvisibleDebuff>();
            invisibleDebuff.ApplyDebuff(true);
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
            var invisibleDebuff = GetComponent<InvisibleDebuff>();
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
    }
}