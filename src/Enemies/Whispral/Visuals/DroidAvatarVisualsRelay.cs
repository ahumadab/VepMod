using System.Collections.Generic;
using UnityEngine;
using VepMod.VepFramework;

#pragma warning disable CS8618

namespace VepMod.Enemies.Whispral.Visuals;

/// <summary>
///     Receveur d'AnimationEvents pour le clone des visuels joueur posé sur un droid.
///     Reproduit les 15 méthodes de PlayerAvatarVisuals attendues par les clips, sans
///     dépendance à playerAvatar / playerCosmetics (qui sont détruits sur le clone).
///     Doit être posé sur le même GameObject que l'Animator.
/// </summary>
public sealed class DroidAvatarVisualsRelay : MonoBehaviour
{
    private static readonly VepLogger LOG = VepLogger.Create<DroidAvatarVisualsRelay>();

    private CosmeticCustomSpring[] _customSprings = System.Array.Empty<CosmeticCustomSpring>();
    private DroidController _droid;
    private CosmeticSprings[] _springs = System.Array.Empty<CosmeticSprings>();

    // Cache des types par ressort. Les Cosmetic MonoBehaviours sont détruits sur le clone
    // (leurs champs internes playerCosmetics/cosmeticAsset sont null et NRE en cascade), du
    // coup spring.cosmetic.type n'est plus accessible. On capture le type AVANT destruction
    // et on le passe au relais via Initialize.
    private Dictionary<CosmeticSprings, SemiFunc.CosmeticType> _springTypes = new();

    public void Initialize(
        DroidController droid,
        GameObject cloneRoot,
        Dictionary<CosmeticSprings, SemiFunc.CosmeticType> springTypes)
    {
        _droid = droid;
        _springTypes = springTypes ?? new Dictionary<CosmeticSprings, SemiFunc.CosmeticType>();
        if (cloneRoot != null)
        {
            _springs = cloneRoot.GetComponentsInChildren<CosmeticSprings>(true);
            _customSprings = cloneRoot.GetComponentsInChildren<CosmeticCustomSpring>(true);
        }
    }

    private bool TryGetSpringType(CosmeticSprings spring, out SemiFunc.CosmeticType type)
    {
        return _springTypes.TryGetValue(spring, out type);
    }

    // ---- Footsteps ----

    public void FootstepLight() => PlayFootstep();
    public void FootstepMedium() => PlayFootstep();
    public void FootstepHeavy() => PlayFootstep();

    // ---- Pose changes (no-op sur le droid) ----

    public void StandToCrouch() { }
    public void CrouchToStand() { }
    public void CrouchToCrawl() { }
    public void CrawlToCrouch() { }

    // ---- Spring foot impulses (reproduit PlayerCosmetics.CosmeticSpringFoot*) ----

    public void RightFootUp() => ImpulseRight(2f, -30f);
    public void RightFootDown() => ImpulseRight(-2f, 30f);
    public void RightFootUpSlow() => ImpulseRight(1f, 30f);
    public void RightFootDownSlow() => ImpulseRight(-1f, -30f);

    public void LeftFootUp() => ImpulseLeft(2f, -30f);
    public void LeftFootDown() => ImpulseLeft(-2f, 30f);
    public void LeftFootUpSlow() => ImpulseLeft(1f, -30f);
    public void LeftFootDownSlow() => ImpulseLeft(-1f, 30f);

    private void PlayFootstep()
    {
        if (_droid != null) _droid.PlayMediumFootstep();
    }

    private void ImpulseRight(float springForce, float customForce)
    {
        foreach (var spring in _springs)
        {
            if (spring == null) continue;
            if (!TryGetSpringType(spring, out var type)) continue;
            if (type == SemiFunc.CosmeticType.LegRight
                || type == SemiFunc.CosmeticType.LegRightMesh
                || type == SemiFunc.CosmeticType.FootRight)
            {
                spring.Impulse(springForce);
            }
        }

        foreach (var custom in _customSprings)
        {
            if (custom != null && custom.type == CosmeticCustomSpring.Type.FootRight)
            {
                custom.Impulse(customForce);
            }
        }
    }

    private void ImpulseLeft(float springForce, float customForce)
    {
        foreach (var spring in _springs)
        {
            if (spring == null) continue;
            if (!TryGetSpringType(spring, out var type)) continue;
            if (type == SemiFunc.CosmeticType.LegLeft
                || type == SemiFunc.CosmeticType.LegLeftMesh
                || type == SemiFunc.CosmeticType.FootLeft)
            {
                spring.Impulse(springForce);
            }
        }

        foreach (var custom in _customSprings)
        {
            if (custom != null && custom.type == CosmeticCustomSpring.Type.FootLeft)
            {
                custom.Impulse(customForce);
            }
        }
    }
}
