using Unity.VisualScripting;
using UnityEngine;
using VepMod.VepFramework;

namespace VepMod.Enemies.Whispral;

/// <summary>
///     Counts the Whisprals attached to one player and turns the Effects on with the first Attachment and off with
///     the last. Every client keeps its own count, driven by the attachment RPC.
/// </summary>
public sealed class WhispralDebuffManager : MonoBehaviour
{
    private static readonly VepLogger LOG = VepLogger.Create<WhispralDebuffManager>();

    private PlayerAvatar playerAvatar = null!;

    public int AttachedCount { get; private set; }

    public bool HasWhispralAttached => AttachedCount > 0;

    private void Awake()
    {
        playerAvatar = GetComponent<PlayerAvatar>();
    }

    private void OnDestroy()
    {
        if (HasWhispralAttached)
        {
            LOG.Debug("WhispralDebuffManager destroyed while debuffs active, forcing cleanup.");
            AttachedCount = 0;
            OnDebuffsDeactivated();
        }
    }

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

        // In singleplayer the local player is the Victim; Vanish and Hallucinations skip themselves there.
        var isLocalPlayer = !SemiFunc.IsMultiplayer() ||
                            (playerAvatar && playerAvatar.photonView && playerAvatar.photonView.IsMine);

        if (isLocalPlayer)
        {
            var invisibleDebuff = gameObject.GetOrAddComponent<InvisiblePlayerDebuff>();
            invisibleDebuff.ApplyDebuff(true);

            var hallucinationDebuff = gameObject.GetOrAddComponent<DroidDebuff>();
            hallucinationDebuff.ApplyDebuff(true, invisibleDebuff);
        }
        else
        {
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

            if (hallucinationDebuff)
            {
                hallucinationDebuff.ApplyDebuff(false, invisibleDebuff);
            }

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
