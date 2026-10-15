using System.Collections.Generic;
using UnityEngine;
using VepMod.VepFramework;

#pragma warning disable CS8618

namespace VepMod.Enemies.Whispral.Visuals;

/// <summary>
///     Receives the AnimationEvents of a Hallucination's cloned player visuals: the 15 PlayerAvatarVisuals methods
///     the animations call, without playerAvatar or playerCosmetics, which are destroyed on the clone.
///     Must sit on the Animator's GameObject.
/// </summary>
public sealed class HallucinationVisualsRelay : MonoBehaviour
{
    private static readonly VepLogger LOG = VepLogger.Create<HallucinationVisualsRelay>();

    private CosmeticCustomSpring[] _customSprings = System.Array.Empty<CosmeticCustomSpring>();
    private Hallucination _hallucination;
    private CosmeticSprings[] _springs = System.Array.Empty<CosmeticSprings>();

    // The Cosmetic MonoBehaviours are destroyed on the clone (their null playerCosmetics and cosmeticAsset throw
    // in cascade), so spring.cosmetic.type is gone: each spring's type is captured before the destruction and
    // handed over through Initialize.
    private Dictionary<CosmeticSprings, SemiFunc.CosmeticType> _springTypes = new();

    public void Initialize(
        Hallucination hallucination,
        GameObject cloneRoot,
        Dictionary<CosmeticSprings, SemiFunc.CosmeticType> springTypes)
    {
        _hallucination = hallucination;
        _springTypes = springTypes ?? new Dictionary<CosmeticSprings, SemiFunc.CosmeticType>();
        if (cloneRoot != null)
        {
            _springs = cloneRoot.GetComponentsInChildren<CosmeticSprings>(true);
            _customSprings = cloneRoot.GetComponentsInChildren<CosmeticCustomSpring>(true);
        }

        var typed = 0;
        foreach (var spring in _springs)
        {
            if (_springTypes.ContainsKey(spring)) typed++;
        }

        LOG.Debug($"Initialize: springs={_springs.Length}, typed={typed}, customSprings={_customSprings.Length}");
    }

    private bool TryGetSpringType(CosmeticSprings spring, out SemiFunc.CosmeticType type)
    {
        return _springTypes.TryGetValue(spring, out type);
    }

    // ---- Footsteps ----

    public void FootstepLight() => PlayFootstep();
    public void FootstepMedium() => PlayFootstep();
    public void FootstepHeavy() => PlayFootstep();

    // ---- Pose changes (no-op on a Hallucination) ----

    public void StandToCrouch() { }
    public void CrouchToStand() { }
    public void CrouchToCrawl() { }
    public void CrawlToCrouch() { }

    // ---- Spring foot impulses (as PlayerCosmetics.CosmeticSpringFoot*) ----

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
        if (_hallucination != null) _hallucination.PlayMediumFootstep();
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
