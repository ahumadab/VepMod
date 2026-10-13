using Unity.VisualScripting;
using UnityEngine;
using VepMod.VepFramework;

namespace VepMod.Enemies.Whispral;

/// <summary>
///     The Haunted state of one player, on every client, driven by the Master's Attachment RPC.
/// </summary>
public sealed class Haunting : MonoBehaviour
{
    private static readonly VepLogger LOG = VepLogger.Create<Haunting>();

    private readonly Attachments attachments = new();
    private PlayerAvatar victim = null!;

    private void Awake()
    {
        victim = GetComponent<PlayerAvatar>();
    }

    private void OnDestroy()
    {
        if (attachments.IsHaunted)
        {
            StopEffects();
        }
    }

    public void Attach(int whispralViewID)
    {
        var transition = attachments.Attach(whispralViewID);
        LOG.Debug($"Whispral {whispralViewID} attached to {victim.playerName}: {transition}");
        Apply(transition);
    }

    public void Detach(int whispralViewID)
    {
        var transition = attachments.Detach(whispralViewID);
        LOG.Debug($"Whispral {whispralViewID} detached from {victim.playerName}: {transition}");
        Apply(transition);
    }

    private void Apply(HauntedTransition transition)
    {
        switch (transition)
        {
            case HauntedTransition.BecameHaunted:
                StartEffects();
                break;
            case HauntedTransition.StoppedBeingHaunted:
                StopEffects();
                break;
        }
    }

    private bool IsVictimsClient()
    {
        return !SemiFunc.IsMultiplayer() || (victim && victim.photonView && victim.photonView.IsMine);
    }

    private void StartEffects()
    {
        LOG.Debug($"{victim.playerName} is Haunted.");

        if (IsVictimsClient())
        {
            gameObject.GetOrAddComponent<Imitations>().Begin();
        }
        else
        {
            gameObject.GetOrAddComponent<DilatedPupils>().Apply(true);
        }
    }

    private void StopEffects()
    {
        LOG.Debug($"{victim.playerName} is no longer Haunted.");

        if (IsVictimsClient())
        {
            var imitations = GetComponent<Imitations>();
            if (imitations)
            {
                imitations.End();
            }
        }
        else
        {
            var dilatedPupils = GetComponent<DilatedPupils>();
            if (dilatedPupils)
            {
                dilatedPupils.Apply(false);
            }
        }
    }
}
