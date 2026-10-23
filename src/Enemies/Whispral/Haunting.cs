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

    private readonly Attachments _attachments = new();
    private PlayerAvatar _victim = null!;

    private void Awake()
    {
        _victim = GetComponent<PlayerAvatar>();
    }

    private void OnDestroy()
    {
        if (_attachments.IsHaunted)
        {
            StopEffects();
        }
    }

    public void Attach(int whispralViewID)
    {
        var transition = _attachments.Attach(whispralViewID);
        LOG.Debug($"Whispral {whispralViewID} attached to {_victim.playerName}: {transition}");
        Apply(transition);
    }

    public void Detach(int whispralViewID)
    {
        var transition = _attachments.Detach(whispralViewID);
        LOG.Debug($"Whispral {whispralViewID} detached from {_victim.playerName}: {transition}");
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
        return !SemiFunc.IsMultiplayer() || (_victim && _victim.photonView && _victim.photonView.IsMine);
    }

    private void StartEffects()
    {
        LOG.Debug($"{_victim.playerName} is Haunted.");

        if (IsVictimsClient())
        {
            gameObject.GetOrAddComponent<Imitations>().Begin();
            Whispers.Local.Begin(_victim);
        }
        else
        {
            gameObject.GetOrAddComponent<DilatedPupils>().Apply(true);
        }
    }

    private void StopEffects()
    {
        LOG.Debug($"{_victim.playerName} is no longer Haunted.");

        if (IsVictimsClient())
        {
            var imitations = GetComponent<Imitations>();
            if (imitations)
            {
                imitations.End();
            }

            Whispers.Local.End();
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
