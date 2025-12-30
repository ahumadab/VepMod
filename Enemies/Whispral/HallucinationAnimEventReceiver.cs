using UnityEngine;

namespace VepMod.Enemies.Whispral;

/// <summary>
///     Reçoit les Animation Events de l'Animator du joueur copié
///     et joue les sons de pas appropriés via le système Materials du jeu.
/// </summary>
public sealed class HallucinationAnimEventReceiver : MonoBehaviour
{
    private HallucinationPlayer hallucinationPlayer;

    public void Initialize(HallucinationPlayer owner)
    {
        hallucinationPlayer = owner;
    }

    #region Animation Event Callbacks - Appelés par l'Animator via Animation Events

    // Ces méthodes sont appelées par les Animation Events de l'animator du joueur
    // Les noms doivent correspondre exactement à ceux définis dans PlayerAvatarVisuals

    public void FootstepLight()
    {
        PlayFootstep(Materials.SoundType.Light);
    }

    public void FootstepMedium()
    {
        PlayFootstep(Materials.SoundType.Medium);
    }

    public void FootstepHeavy()
    {
        PlayFootstep(Materials.SoundType.Heavy);
    }

    // Autres events d'animation qui pourraient être appelés mais qu'on ignore
    public void StandToCrouch() { }
    public void CrouchToStand() { }
    public void CrouchToCrawl() { }
    public void CrawlToCrouch() { }

    #endregion

    private void PlayFootstep(Materials.SoundType soundType)
    {
        if (!hallucinationPlayer) return;

        var position = transform.position;

        // Récupérer le MaterialTrigger du joueur source si disponible
        Materials.MaterialTrigger trigger = null;
        if (hallucinationPlayer.SourcePlayer)
        {
            trigger = hallucinationPlayer.SourcePlayer.MaterialTrigger;
        }

        // Utiliser le système Materials du jeu pour jouer le son de pas
        // Le HostType.OtherPlayer fait que le son est spatialisé (3D)
        Materials.Instance.Impulse(
            position,
            Vector3.down,
            soundType,
            footstep: true,
            footstepParticles: true,
            trigger,
            Materials.HostType.OtherPlayer
        );
    }
}
