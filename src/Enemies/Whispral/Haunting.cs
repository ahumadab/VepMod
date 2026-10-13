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
        Apply(attachments.Attach(whispralViewID));
    }

    public void Detach(int whispralViewID)
    {
        Apply(attachments.Detach(whispralViewID));
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

        // In singleplayer the local player is the Victim; Vanish and Hallucinations skip themselves there.
        if (IsVictimsClient())
        {
            var vanish = gameObject.GetOrAddComponent<InvisiblePlayerDebuff>();
            vanish.ApplyDebuff(true);
            gameObject.GetOrAddComponent<DroidDebuff>().ApplyDebuff(true, vanish);
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
            var vanish = GetComponent<InvisiblePlayerDebuff>();
            var hallucinations = GetComponent<DroidDebuff>();
            if (hallucinations)
            {
                hallucinations.ApplyDebuff(false, vanish);
            }

            if (vanish)
            {
                vanish.ApplyDebuff(false);
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
